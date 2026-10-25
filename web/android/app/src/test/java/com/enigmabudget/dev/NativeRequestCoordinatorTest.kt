package com.enigmabudget.dev

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertThrows
import org.junit.Assert.assertTrue
import org.junit.Test
import java.util.concurrent.CompletableFuture
import java.util.concurrent.CountDownLatch
import java.util.concurrent.ExecutionException
import java.util.concurrent.Executors
import java.util.concurrent.TimeUnit

class NativeRequestCoordinatorTest {
    @Test fun Native_request_aborts_when_context_changes_after_permit_before_dispatch() {
        val source = FakeCredentials()
        var sent = 0
        val coordinator = NativeRequestCoordinator(NativeRequestGate("https://api.example.test"), source, NativeHttpTransport { _, _ ->
            sent++
            CompletableFuture.completedFuture(ok())
        })
        val first = coordinator.activateContext(1, "user-1", "space-a")
        val permit = coordinator.prepare(first, "one", req("space-a"))
        coordinator.activateContext(2, "user-1", "space-b")
        assertThrows(SecurityException::class.java) { coordinator.dispatch(permit) }
        assertEquals(0, sent)
        assertThrows(SecurityException::class.java) { coordinator.activateContext(1, "user-1", "space-a") }
    }

    @Test fun Refresh_during_prepare_does_not_dispatch() {
        var refreshes = 0
        var sends = 0
        val source = FakeCredentials(expired = true) { refreshes++ }
        val coordinator = NativeRequestCoordinator(NativeRequestGate("https://api.example.test"), source, NativeHttpTransport { _, _ ->
            sends++
            CompletableFuture.completedFuture(ok())
        }, clock = { 10_000 })
        val context = coordinator.activateContext(5, "user-1", "space-a")
        coordinator.prepare(context, "one", req("space-a"))
        assertEquals(1, refreshes)
        assertEquals(1, source.refreshCommits)
        assertEquals(0, sends)
    }

    @Test fun Retry_permit_requires_auth_middleware_marker_and_is_single_use() {
        var sends = 0
        val coordinator = NativeRequestCoordinator(NativeRequestGate("https://api.example.test"), FakeCredentials(), NativeHttpTransport { _, _ ->
            sends++
            CompletableFuture.completedFuture(if (sends == 1) NativeApiResponse(401, mapOf("www-authenticate" to "Bearer error=\"invalid_token\"", "content-type" to "application/json"), "{}") else ok())
        })
        val context = coordinator.activateContext(1, "user-1", "space-a")
        val first = coordinator.prepare(context, "r1", req("space-a"))
        coordinator.dispatch(first)
        assertThrows(SecurityException::class.java) { coordinator.dispatch(first) }
        val retry = coordinator.prepareUnauthorizedRetry(context, "r1", first)
        assertEquals(1, sends)
        coordinator.dispatch(retry)
        assertEquals(2, sends)
        assertThrows(SecurityException::class.java) { coordinator.dispatch(retry) }

        val ordinary = NativeRequestCoordinator(NativeRequestGate("https://api.example.test"), FakeCredentials(), NativeHttpTransport { _, _ ->
            CompletableFuture.completedFuture(NativeApiResponse(401, mapOf("content-type" to "application/json"), "{}"))
        })
        val ordinaryContext = ordinary.activateContext(1, "user-1", null)
        val ordinaryPermit = ordinary.prepare(ordinaryContext, "r2", NativeApiRequest("https://api.example.test/api/me", "GET", emptyMap()))
        ordinary.dispatch(ordinaryPermit)
        assertThrows(SecurityException::class.java) { ordinary.prepareUnauthorizedRetry(ordinaryContext, "r2", ordinaryPermit) }
    }

    @Test fun Identity_is_taken_from_the_trusted_native_session() {
        val coordinator = NativeRequestCoordinator(NativeRequestGate("https://api.example.test"), FakeCredentials(), NativeHttpTransport { _, _ -> CompletableFuture.completedFuture(ok()) })
        assertThrows(SecurityException::class.java) { coordinator.activateContext(1, "attacker-selected", null) }
    }

    @Test fun Invalidate_and_logout_clearing_complete_while_transport_is_blocked_and_discard_late_response() {
        val started = CountDownLatch(1)
        val response = CompletableFuture<NativeApiResponse>()
        val source = FakeCredentials()
        val coordinator = NativeRequestCoordinator(NativeRequestGate("https://api.example.test"), source, NativeHttpTransport { _, _ ->
            started.countDown()
            response
        })
        val context = coordinator.activateContext(1, "user-1", null)
        val permit = coordinator.prepare(context, "blocked", NativeApiRequest("https://api.example.test/api/me", "GET", emptyMap()))
        val worker = Executors.newSingleThreadExecutor()
        try {
            val dispatch = worker.submit<NativeDispatchResult> { coordinator.dispatch(permit) }
            assertTrue(started.await(2, TimeUnit.SECONDS))
            val localClear = coordinator.invalidate {
                source.clear()
                "local-session-cleared"
            }
            assertEquals("local-session-cleared", localClear)
            assertNull(source.credentials())
            response.complete(ok())
            assertThrows(ExecutionException::class.java) { dispatch.get(2, TimeUnit.SECONDS) }
            assertEquals(0, source.refreshCommits)
        } finally { worker.shutdownNow() }
    }

    @Test fun Late_refresh_after_logout_does_not_commit_credentials_or_dispatch_request() {
        val refreshStarted = CountDownLatch(1)
        val releaseRefresh = CountDownLatch(1)
        val source = FakeCredentials(expired = true) {
            refreshStarted.countDown()
            releaseRefresh.await(2, TimeUnit.SECONDS)
        }
        var sends = 0
        val coordinator = NativeRequestCoordinator(NativeRequestGate("https://api.example.test"), source, NativeHttpTransport { _, _ ->
            sends++
            CompletableFuture.completedFuture(ok())
        }, clock = { 10_000 })
        val context = coordinator.activateContext(1, "user-1", null)
        val worker = Executors.newSingleThreadExecutor()
        try {
            val prepare = worker.submit<NativePermit> { coordinator.prepare(context, "refreshing", NativeApiRequest("https://api.example.test/api/me", "GET", emptyMap())) }
            assertTrue(refreshStarted.await(2, TimeUnit.SECONDS))
            coordinator.invalidate {
                source.clear()
                Unit
            }
            assertNull(source.credentials())
            releaseRefresh.countDown()
            assertThrows(ExecutionException::class.java) { prepare.get(2, TimeUnit.SECONDS) }
            assertEquals(0, source.refreshCommits)
            assertEquals(0, sends)
        } finally {
            releaseRefresh.countDown()
            worker.shutdownNow()
        }
    }

    private fun req(workspace: String) = NativeApiRequest("https://api.example.test/api/me", "GET", mapOf("X-Workspace-Id" to workspace))
    private fun ok() = NativeApiResponse(200, mapOf("content-type" to "application/json"), "{}")

    private class FakeCredentials(
        private val expired: Boolean = false,
        private val onRefresh: () -> Unit = {},
    ) : NativeCredentialSource {
        private var userId: String? = "user-1"
        private var value: NativeCredentials? = NativeCredentials("access-1", "refresh-1", if (expired) 0 else Long.MAX_VALUE)
        var refreshCommits = 0
            private set

        override fun trustedUserId() = userId
        override fun credentials() = value
        override fun refresh(current: NativeCredentials): NativeCredentials {
            onRefresh()
            return NativeCredentials("access-2", "refresh-2", Long.MAX_VALUE)
        }
        override fun commitRefresh(value: NativeCredentials): Boolean {
            if (this.value == null) return false
            this.value = value
            refreshCommits++
            return true
        }
        fun clear() { userId = null; value = null }
    }
}
