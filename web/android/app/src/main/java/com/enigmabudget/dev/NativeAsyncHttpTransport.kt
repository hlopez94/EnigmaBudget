package com.enigmabudget.dev

import java.util.concurrent.CancellationException
import java.util.concurrent.CompletableFuture

interface NativeAsyncHttpCallback {
    fun onResponse(response: NativeApiResponse)
    fun onFailure(error: Throwable)
}

interface NativeAsyncHttpCall {
    /** Must return after the HTTP client accepts the call; response/network waits happen in callbacks. */
    fun enqueue(callback: NativeAsyncHttpCallback)
    fun cancel()
}

fun interface NativeAsyncHttpClient {
    fun newCall(request: GatedRequest, bearer: String): NativeAsyncHttpCall
}

/** Serializes client acceptance and cancellation with coordinator session/context invalidation. */
class NativeAsyncHttpTransport(private val client: NativeAsyncHttpClient) : NativeHttpTransport {
    private class Pending(
        val result: CompletableFuture<NativeApiResponse>,
        var request: GatedRequest?,
        var bearer: String?,
        var call: NativeAsyncHttpCall? = null,
    ) {
        fun clearPayload() { request = null; bearer = null }
    }

    private val stateLock = Any()
    private val calls = linkedSetOf<Pending>()

    override fun dispatch(request: GatedRequest, bearer: String): CompletableFuture<NativeApiResponse> {
        val pending = Pending(CompletableFuture(), request, bearer)
        synchronized(stateLock) {
            calls += pending
            try {
                val call = client.newCall(request, bearer)
                pending.call = call
                // The coordinator holds its short state lock here. Invalidation cannot pass this
                // point until the actual HTTP client has accepted the call with enqueue().
                call.enqueue(object : NativeAsyncHttpCallback {
                    override fun onResponse(response: NativeApiResponse) = finish(pending, response, null)
                    override fun onFailure(error: Throwable) = finish(pending, null, error)
                })
                pending.clearPayload()
            } catch (error: Throwable) {
                calls.remove(pending)
                pending.clearPayload()
                pending.call = null
                pending.result.completeExceptionally(error)
            }
        }
        return pending.result
    }

    /** Called under the coordinator lock for both session logout/replacement and context changes. */
    fun cancelPending() {
        val canceled = synchronized(stateLock) {
            calls.toList().also { pending ->
                calls.clear()
                pending.forEach { it.clearPayload() }
            }
        }
        canceled.forEach { pending ->
            val call = synchronized(stateLock) { pending.call.also { pending.call = null } }
            try { call?.cancel() } catch (_: Exception) { }
            pending.result.completeExceptionally(CancellationException("Native request was invalidated before completion."))
        }
    }

    internal fun pendingCallCount(): Int = synchronized(stateLock) { calls.size }

    private fun finish(pending: Pending, response: NativeApiResponse?, error: Throwable?) {
        val accepted = synchronized(stateLock) {
            if (!calls.remove(pending)) false else {
                pending.call = null
                pending.clearPayload()
                true
            }
        }
        if (!accepted) return
        if (error != null) pending.result.completeExceptionally(error)
        else pending.result.complete(response ?: NativeApiResponse(500, emptyMap(), ""))
    }
}
