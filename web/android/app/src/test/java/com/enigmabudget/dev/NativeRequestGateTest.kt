package com.enigmabudget.dev

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertThrows
import org.junit.Assert.assertTrue
import org.junit.Test
import java.util.concurrent.CompletableFuture

class NativeRequestGateTest {
    private val gate = NativeRequestGate("https://api.example.test")

    @Test fun Generic_auth_paths_are_denied() {
        var sends = 0
        val coordinator = NativeRequestCoordinator(gate, FakeCredentials(), NativeHttpTransport { _, _ ->
            sends++
            CompletableFuture.completedFuture(NativeApiResponse(200, mapOf("content-type" to "application/json"), "{}"))
        })
        val context = coordinator.activateContext(1, "user-1", null)
        listOf("https://api.example.test/api/auth/native/session", "https://api.example.test/API/AUTH/logout").forEachIndexed { index, url ->
            assertThrows(NativeRequestRejected::class.java) { coordinator.prepare(context, "auth-$index", NativeApiRequest(url, "GET", emptyMap())) }
        }
        assertEquals(0, sends)
        assertTrue(gate.validate(NativeApiRequest("https://api.example.test/api/authx/items", "GET", emptyMap())).url.path.contains("authx"))
    }

    @Test fun Encoded_auth_path_cannot_bypass_block() {
        var sends = 0
        val coordinator = NativeRequestCoordinator(gate, FakeCredentials(), NativeHttpTransport { _, _ ->
            sends++
            CompletableFuture.completedFuture(NativeApiResponse(200, mapOf("content-type" to "application/json"), "{}"))
        })
        val context = coordinator.activateContext(1, "user-1", null)
        listOf("https://api.example.test/api%2fauth/native/session", "https://api.example.test/api/%2e%2e/api/auth/session", "https://api.example.test/API%252fAUTH/session")
            .forEachIndexed { index, url -> assertThrows(NativeRequestRejected::class.java) { coordinator.prepare(context, "encoded-$index", NativeApiRequest(url, "GET", emptyMap())) } }
        assertEquals(0, sends)
    }

    @Test fun Caller_auth_and_cookie_headers_are_rejected() {
        listOf("Authorization", "cOoKiE", "HOST").forEach { header ->
            assertThrows(NativeRequestRejected::class.java) { gate.validate(NativeApiRequest("https://api.example.test/api/me", "GET", mapOf(header to "caller-value"))) }
        }
        assertFalse(gate.validate(NativeApiRequest("https://api.example.test/api/me", "POST", mapOf("Content-Type" to "application/json", "X-CSRF-TOKEN" to "ignored"), "{}"))
            .headers.keys.any { it.equals("X-CSRF-TOKEN", true) })
        var sends = 0
        val coordinator = NativeRequestCoordinator(gate, FakeCredentials(), NativeHttpTransport { _, _ ->
            sends++
            CompletableFuture.completedFuture(NativeApiResponse(200, mapOf("content-type" to "application/json"), "{}"))
        })
        val context = coordinator.activateContext(1, "user-1", null)
        assertThrows(NativeRequestRejected::class.java) { coordinator.prepare(context, "credentials", NativeApiRequest("https://api.example.test/api/me", "GET", mapOf("Authorization" to "caller"))) }
        assertEquals(0, sends)
    }

    @Test fun Token_shaped_response_is_denied() {
        listOf("{\"user\":{\"RefreshToken\":\"secret\"}}", "{\"nested\":{\"access\\u0054oken\":\"secret\"}}", "{} {\"refreshToken\":\"secret\"}").forEach { body ->
            assertThrows(NativeRequestRejected::class.java) {
                gate.validateResponse(java.net.URI("https://api.example.test/api/me"), NativeApiResponse(200, mapOf("content-type" to "application/json"), body))
            }
        }
        val filtered = gate.validateResponse(java.net.URI("https://api.example.test/api/me"), NativeApiResponse(200, mapOf("content-type" to "application/json", "Set-Cookie" to "sid=x", "Authorization" to "Bearer x"), "{}"))
        assertTrue(filtered.headers.keys.none { it.equals("Set-Cookie", true) || it.equals("Authorization", true) })
    }

    @Test fun Structured_problem_responses_keep_status_and_original_JSON_body() {
        listOf(403, 409).forEach { status ->
            val original = "{\"title\":\"Conflicto\",\"detail\":\"La versión cambió.\",\"status\":$status}"
            val response = gate.validateResponse(java.net.URI("https://api.example.test/api/finance"), NativeApiResponse(status, mapOf("content-type" to "application/problem+json; charset=utf-8"), original))
            assertEquals(status, response.status)
            assertEquals(original, response.body)
        }
    }

    @Test fun Cross_origin_redirect_never_receives_authorization() {
        var sends = 0
        val source = FakeCredentials()
        val transport = NativeHttpTransport { _, bearer ->
            sends++
            assertEquals("access-1", bearer)
            CompletableFuture.completedFuture(NativeApiResponse(302, mapOf("location" to "https://evil.example/collect", "content-type" to "application/json"), "{}"))
        }
        val coordinator = NativeRequestCoordinator(gate, source, transport)
        val context = coordinator.activateContext(1, "user-1", null)
        val permit = coordinator.prepare(context, "r1", NativeApiRequest("https://api.example.test/api/me", "GET", emptyMap()))
        assertThrows(NativeRequestRejected::class.java) { coordinator.dispatch(permit) }
        assertEquals(1, sends)
    }

    private class FakeCredentials : NativeCredentialSource {
        override fun trustedUserId() = "user-1"
        override fun credentials() = NativeCredentials("access-1", "refresh-1", Long.MAX_VALUE)
        override fun refresh(current: NativeCredentials) = current
        override fun commitRefresh(value: NativeCredentials) = true
    }
}
