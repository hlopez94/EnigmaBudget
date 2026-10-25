package com.enigmabudget.dev

import java.util.concurrent.CancellationException
import java.util.concurrent.CompletableFuture
import java.util.concurrent.Executor

/**
 * Keeps protected request secrets in a cancellable queue until a worker claims the HTTP handoff.
 * Invalidation removes queued payload references; an already claimed handoff may finish normally.
 */
class NativeQueuedHttpTransport(
    private val executor: Executor,
    private val handoffToHttp: (GatedRequest, String) -> NativeApiResponse,
) : NativeHttpTransport {
    private class Payload(var request: GatedRequest?, var bearer: String?) {
        fun clear() { request = null; bearer = null }
    }

    private class PendingCall(
        val generation: Long,
        val result: CompletableFuture<NativeApiResponse>,
        var payload: Payload?,
    )

    private val queueLock = Any()
    private val queued = linkedSetOf<PendingCall>()
    private var generation = 0L

    override fun dispatch(request: GatedRequest, bearer: String): CompletableFuture<NativeApiResponse> {
        val pending = synchronized(queueLock) {
            PendingCall(generation, CompletableFuture(), Payload(request, bearer)).also(queued::add)
        }
        try {
            executor.execute { runPending(pending) }
        } catch (error: Throwable) {
            synchronized(queueLock) {
                queued.remove(pending)
                pending.payload?.clear()
                pending.payload = null
            }
            pending.result.completeExceptionally(error)
        }
        return pending.result
    }

    /** Called atomically inside the coordinator's session invalidation lock. */
    fun invalidateQueued() {
        val canceled = synchronized(queueLock) {
            generation++
            queued.toList().also { pending ->
                queued.clear()
                pending.forEach {
                    it.payload?.clear()
                    it.payload = null
                }
            }
        }
        canceled.forEach { it.result.completeExceptionally(CancellationException("Native request was invalidated before HTTP handoff.")) }
    }

    internal fun retainedQueuedPayloadCount(): Int = synchronized(queueLock) { queued.count { it.payload != null } }

    private fun runPending(pending: PendingCall) {
        val payload = synchronized(queueLock) {
            if (pending.generation != generation || pending !in queued || pending.payload == null) {
                queued.remove(pending)
                pending.payload?.clear()
                pending.payload = null
                null
            } else {
                // Removing the payload under this lock is the HTTP handoff boundary. Invalidation
                // cancels work still in queued; after this point the request may finish unknown.
                queued.remove(pending)
                pending.payload.also { pending.payload = null }
            }
        }
        if (payload == null) {
            pending.result.completeExceptionally(CancellationException("Native request was invalidated before HTTP handoff."))
            return
        }

        try {
            val request = payload.request ?: throw CancellationException("Native request was cleared before HTTP handoff.")
            val bearer = payload.bearer ?: throw CancellationException("Native request was cleared before HTTP handoff.")
            pending.result.complete(handoffToHttp(request, bearer))
        } catch (error: Throwable) {
            pending.result.completeExceptionally(error)
        } finally {
            payload.clear()
        }
    }
}
