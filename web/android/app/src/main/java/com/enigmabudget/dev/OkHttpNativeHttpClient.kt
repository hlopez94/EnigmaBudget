package com.enigmabudget.dev

import okhttp3.Authenticator
import okhttp3.Call
import okhttp3.Callback
import okhttp3.CookieJar
import okhttp3.Dispatcher
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.OkHttpClient
import okhttp3.Protocol
import okhttp3.Request
import okhttp3.RequestBody
import okhttp3.Response
import okio.BufferedSink
import java.io.IOException
import java.util.UUID
import java.util.concurrent.ArrayBlockingQueue
import java.util.concurrent.TimeUnit
import java.util.concurrent.ThreadPoolExecutor

class OkHttpNativeHttpClient(
    private val client: OkHttpClient = newNativeOkHttpClient(),
) : NativeAsyncHttpClient {
    override fun newCall(request: GatedRequest, bearer: String): NativeAsyncHttpCall {
        val builder = Request.Builder().url(request.url.toASCIIString())
        request.headers.forEach { (name, value) ->
            if (!name.equals("authorization", true) && !name.equals("cookie", true) && !name.equals("host", true)) {
                builder.header(name, value)
            }
        }
        builder.header("Authorization", "Bearer $bearer")
        val mediaType = (request.headers["content-type"] ?: "application/json").toMediaType()
        val body = request.body?.toByteArray(Charsets.UTF_8)?.let { OneShotJsonBody(it, mediaType) }
            ?: if (request.method in setOf("POST", "PUT", "PATCH")) OneShotJsonBody(byteArrayOf(), mediaType) else null
        builder.method(request.method, body)
        return OkHttpNativeCall(client.newCall(builder.build()))
    }

    private class OneShotJsonBody(private val bytes: ByteArray, private val mediaType: okhttp3.MediaType) : RequestBody() {
        override fun contentType() = mediaType
        override fun contentLength() = bytes.size.toLong()
        override fun isOneShot() = true
        override fun writeTo(sink: BufferedSink) { sink.write(bytes) }
    }

    private class OkHttpNativeCall(private val call: Call) : NativeAsyncHttpCall {
        override fun enqueue(callback: NativeAsyncHttpCallback) {
            call.enqueue(object : Callback {
                override fun onFailure(call: Call, error: IOException) = callback.onFailure(error)
                override fun onResponse(call: Call, response: Response) {
                    try {
                        val headers = linkedMapOf<String, String>()
                        for (index in 0 until response.headers.size) {
                            val name = response.headers.name(index).lowercase()
                            val value = response.headers.value(index)
                            headers[name] = headers[name]?.let { "$it, $value" } ?: value
                        }
                        val result = NativeApiResponse(response.code, headers, response.body?.string().orEmpty())
                        callback.onResponse(result)
                    } catch (error: Throwable) {
                        callback.onFailure(error)
                    } finally {
                        response.close()
                    }
                }
            })
        }

        override fun cancel() = call.cancel()
    }
}

private const val SUPPRESS_503_RETRY_AFTER = "X-EnigmaBudget-Internal-Retry-After"
private const val NO_IMMEDIATE_RETRY = "2147483647"

internal fun newNativeOkHttpClient(dispatcher: Dispatcher = boundedDispatcher()): OkHttpClient = OkHttpClient.Builder()
    .dispatcher(dispatcher.apply { maxRequests = 8; maxRequestsPerHost = 4 })
    .callTimeout(12, TimeUnit.SECONDS)
    .connectTimeout(8, TimeUnit.SECONDS)
    .readTimeout(8, TimeUnit.SECONDS)
    .writeTimeout(8, TimeUnit.SECONDS)
    .retryOnConnectionFailure(false)
    .followRedirects(false)
    .followSslRedirects(false)
    .cookieJar(CookieJar.NO_COOKIES)
    .authenticator(Authenticator.NONE)
    .proxyAuthenticator(Authenticator.NONE)
    .addInterceptor { chain ->
        val response = chain.proceed(chain.request())
        val originalRetryAfter = response.header(SUPPRESS_503_RETRY_AFTER)
        val restored = response.newBuilder().removeHeader(SUPPRESS_503_RETRY_AFTER)
        if (originalRetryAfter != null) restored.header("Retry-After", originalRetryAfter)
        restored.build()
    }
    .addNetworkInterceptor { chain ->
        val response = chain.proceed(chain.request())
        if (response.code == 503 && response.header("Retry-After")?.trim()?.toIntOrNull() == 0) {
            response.newBuilder()
                .header(SUPPRESS_503_RETRY_AFTER, response.header("Retry-After")!!)
                .header("Retry-After", NO_IMMEDIATE_RETRY)
                .build()
        } else response
    }
    .build()

private fun boundedDispatcher(): Dispatcher {
    val executor = ThreadPoolExecutor(0, 8, 60L, TimeUnit.SECONDS, ArrayBlockingQueue(8), { runnable ->
        Thread(runnable, "native-http-${UUID.randomUUID()}").apply { isDaemon = true }
    }, ThreadPoolExecutor.AbortPolicy())
    return Dispatcher(executor)
}
