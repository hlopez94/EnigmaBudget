package com.enigmabudget.dev

import org.json.JSONObject
import org.json.JSONTokener

data class NativeAuthOptions(
    val googleConfigured: Boolean,
    val developmentLoginEnabled: Boolean,
)

/** Projects the server response onto the only auth-option fields allowed across the JS bridge. */
object NativeAuthOptionsProjection {
    fun project(response: NativeApiResponse): NativeAuthOptions {
        if (response.status !in 200..299) throw NativeHttpFailure(response.status)
        val contentType = response.headers.entries.firstOrNull { it.key.equals("content-type", true) }
            ?.value?.substringBefore(';')?.trim()?.lowercase()
        if (contentType != "application/json" && contentType?.endsWith("+json") != true) {
            throw IllegalArgumentException("Native auth options response is invalid.")
        }
        try {
            val tokener = JSONTokener(response.body)
            val parsed = tokener.nextValue()
            if (parsed !is JSONObject || tokener.nextClean() != '\u0000') invalid()
            val googleConfigured = parsed.opt("googleConfigured") as? Boolean ?: invalid()
            val developmentLoginEnabled = parsed.opt("developmentLoginEnabled") as? Boolean ?: invalid()
            return NativeAuthOptions(googleConfigured, developmentLoginEnabled)
        } catch (_: Exception) {
            throw IllegalArgumentException("Native auth options response is invalid.")
        }
    }

    private fun invalid(): Nothing = throw IllegalArgumentException("Native auth options response is invalid.")
}
