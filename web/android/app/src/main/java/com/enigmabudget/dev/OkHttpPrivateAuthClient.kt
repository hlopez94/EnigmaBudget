package com.enigmabudget.dev

import okhttp3.MediaType.Companion.toMediaType
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.RequestBody
import okio.BufferedSink

/** Cookieless, non-redirecting, non-replaying transport for private authentication calls. */
class OkHttpPrivateAuthClient(private val client: OkHttpClient = newNativeOkHttpClient()) {
    fun execute(url: String, method: String, headers: Map<String, String>, body: String?, timeoutMillis: Int): NativeApiResponse {
        val callClient = client.newBuilder()
            .connectTimeout(timeoutMillis.toLong(), java.util.concurrent.TimeUnit.MILLISECONDS)
            .readTimeout(timeoutMillis.toLong(), java.util.concurrent.TimeUnit.MILLISECONDS)
            .callTimeout(timeoutMillis.toLong(), java.util.concurrent.TimeUnit.MILLISECONDS)
            .build()
        val builder = Request.Builder().url(url)
        headers.forEach { (name, value) ->
            if (!name.equals("cookie", true) && !name.equals("host", true)) builder.header(name, value)
        }
        val requestBody = body?.toByteArray(Charsets.UTF_8)?.let { bytes ->
            val mediaType = headers.entries.firstOrNull { it.key.equals("content-type", true) }?.value?.toMediaType()
            object : RequestBody() {
                override fun contentType() = mediaType
                override fun contentLength() = bytes.size.toLong()
                override fun isOneShot() = true
                override fun writeTo(sink: BufferedSink) { sink.write(bytes) }
            }
        } ?: if (method.uppercase() in setOf("POST", "PUT", "PATCH")) {
            object : RequestBody() {
                override fun contentType() = "application/json".toMediaType()
                override fun contentLength() = 0L
                override fun isOneShot() = true
                override fun writeTo(sink: BufferedSink) = Unit
            }
        } else null
        val request = builder.method(method, requestBody).build()
        callClient.newCall(request).execute().use { response ->
            val responseHeaders = linkedMapOf<String, String>()
            for (index in 0 until response.headers.size) {
                val name = response.headers.name(index).lowercase()
                val value = response.headers.value(index)
                responseHeaders[name] = responseHeaders[name]?.let { "$it, $value" } ?: value
            }
            return NativeApiResponse(response.code, responseHeaders, response.body?.string().orEmpty())
        }
    }
}
