package com.enigmabudget.dev

import java.net.URI
import java.net.URLDecoder
import java.nio.charset.StandardCharsets
import java.util.Locale
import org.json.JSONArray
import org.json.JSONObject
import org.json.JSONTokener

data class NativeApiRequest(val url: String, val method: String, val headers: Map<String, String>, val body: String? = null)
data class NativeApiResponse(val status: Int, val headers: Map<String, String>, val body: String)
data class GatedRequest(val url: URI, val method: String, val headers: Map<String, String>, val body: String?)

class NativeRequestRejected(message: String) : IllegalArgumentException(message)

/** Shared request and response policy used by production transport and JVM tests. */
class NativeRequestGate(
    apiBaseUrl: String,
    private val allowDebugLocalhost: Boolean = false,
) {
    private val base = URI(apiBaseUrl).normalize()
    private val origin = "${base.scheme.lowercase(Locale.ROOT)}://${base.rawAuthority.lowercase(Locale.ROOT)}"

    init {
        require(base.isAbsolute && base.host != null) { "API base URL must be absolute." }
        require(base.scheme.equals("https", true) || (allowDebugLocalhost && base.scheme.equals("http", true) && isLocalHost(base.host))) {
            "API base URL must use HTTPS."
        }
    }

    fun validate(request: NativeApiRequest): GatedRequest {
        val uri = try { URI(request.url).normalize() } catch (_: Exception) { throw NativeRequestRejected("Invalid request URL.") }
        if (!uri.isAbsolute || uri.userInfo != null || !sameOrigin(uri)) throw NativeRequestRejected("Request origin is not allowed.")
        if (!(uri.scheme.equals("https", true) || (allowDebugLocalhost && uri.scheme.equals("http", true) && isLocalHost(uri.host)))) {
            throw NativeRequestRejected("Native API requests require HTTPS.")
        }
        val path = normalizedPath(uri.rawPath ?: "/")
        if (!path.lowercase(Locale.ROOT).startsWith("/api/")) throw NativeRequestRejected("Only API paths are allowed.")
        if (isAuthPath(path.lowercase(Locale.ROOT))) throw NativeRequestRejected("Authentication paths are reserved for the native plugin.")
        val canonicalUri = try { URI(uri.scheme, uri.authority, path, uri.query, uri.fragment) }
        catch (_: Exception) { throw NativeRequestRejected("Invalid canonical request URL.") }
        val headers = linkedMapOf<String, String>()
        request.headers.forEach { (name, value) ->
            val normalized = name.lowercase(Locale.ROOT)
            if (normalized in setOf("authorization", "cookie", "host")) throw NativeRequestRejected("Caller credentials and host headers are forbidden.")
            if (normalized != "x-csrf-token") {
                if (normalized.equals("content-type") && value.substringBefore(';').trim().lowercase(Locale.ROOT) != "application/json") {
                    throw NativeRequestRejected("Only JSON requests are supported.")
                }
                headers[normalized] = value
            }
        }
        val method = request.method.uppercase(Locale.ROOT)
        if (method !in setOf("GET", "POST", "PUT", "PATCH", "DELETE", "HEAD")) throw NativeRequestRejected("Unsupported HTTP method.")
        if (request.body != null) {
            if (headers["content-type"]?.substringBefore(';')?.trim()?.lowercase(Locale.ROOT) != "application/json") {
                throw NativeRequestRejected("A JSON content type is required for request bodies.")
            }
            rejectTokenFields(request.body)
        }
        return GatedRequest(canonicalUri, method, headers, request.body)
    }

    fun validateResponse(requestUrl: URI, response: NativeApiResponse): NativeApiResponse {
        if (response.status in 300..399) throw NativeRequestRejected("Native API redirects are not followed.")
        response.headers.entries.firstOrNull { it.key.equals("location", true) }?.value?.let { location ->
            val target = try { requestUrl.resolve(location).normalize() } catch (_: Exception) { throw NativeRequestRejected("Invalid redirect target.") }
            if (!sameOrigin(target)) throw NativeRequestRejected("Cross-origin redirects are forbidden.")
        }
        val filtered = response.headers.filterKeys { !it.equals("authorization", true) && !it.equals("set-cookie", true) }
        if (response.body.isNotEmpty()) {
            val contentType = filtered.entries.firstOrNull { it.key.equals("content-type", true) }?.value.orEmpty()
            if (!isJsonResponseType(contentType)) {
                throw NativeRequestRejected("Only JSON responses are supported.")
            }
            rejectTokenFields(response.body)
        }
        return response.copy(headers = filtered)
    }

    private fun sameOrigin(uri: URI): Boolean = "${uri.scheme?.lowercase(Locale.ROOT)}://${uri.rawAuthority?.lowercase(Locale.ROOT)}" == origin

    private fun normalizedPath(rawPath: String): String {
        var path = rawPath
        repeat(4) {
            val decoded = try { percentDecode(path) } catch (_: Exception) { throw NativeRequestRejected("Malformed encoded path.") }
            val normalizedSeparators = decoded.replace('\\', '/')
            if (normalizedSeparators == path) return canonicalPath(path)
            path = normalizedSeparators
        }
        if (path.contains('%')) throw NativeRequestRejected("Excessively encoded path.")
        return canonicalPath(path)
    }

    private fun isAuthPath(path: String): Boolean {
        val segments = path.split('/').filter { it.isNotEmpty() }
        return segments.size >= 2 && segments[0] == "api" && segments[1] == "auth"
    }

    private fun percentDecode(value: String): String {
        // URLDecoder treats '+' as a space; a URL path does not.
        return URLDecoder.decode(value.replace("+", "%2B"), StandardCharsets.UTF_8)
    }

    private fun rejectTokenFields(json: String) {
        val forbidden = setOf("accesstoken", "refreshtoken", "idtoken", "authorization")
        val parsed = try {
            val tokener = JSONTokener(json)
            tokener.nextValue().also { if (tokener.nextClean() != 0.toChar()) throw NativeRequestRejected("Malformed JSON payload.") }
        } catch (error: NativeRequestRejected) { throw error }
        catch (_: Exception) { throw NativeRequestRejected("Malformed JSON payload.") }
        fun inspect(value: Any?) {
            when (value) {
                is JSONObject -> value.keys().forEach { key ->
                    if (key.lowercase(Locale.ROOT) in forbidden) throw NativeRequestRejected("Token-shaped JSON fields are forbidden.")
                    inspect(value.opt(key))
                }
                is JSONArray -> for (index in 0 until value.length()) inspect(value.opt(index))
            }
        }
        inspect(parsed)
        if (parsed == null) throw NativeRequestRejected("JSON payload is required.")
    }

    private fun canonicalPath(path: String): String {
        val segments = mutableListOf<String>()
        path.split('/').forEach { part ->
            when (part) {
                "", "." -> Unit
                ".." -> if (segments.isNotEmpty()) segments.removeAt(segments.lastIndex)
                else -> segments.add(part)
            }
        }
        return "/" + segments.joinToString("/")
    }

    private fun isJsonResponseType(contentType: String): Boolean {
        val mediaType = contentType.substringBefore(';').trim().lowercase(Locale.ROOT)
        return mediaType == "application/json" || mediaType.endsWith("+json")
    }

    private fun isLocalHost(host: String?): Boolean = host.equals("localhost", true) || host == "127.0.0.1" || host == "::1"
}
