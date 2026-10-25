package com.enigmabudget.dev

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import java.util.concurrent.CountDownLatch
import java.util.concurrent.Executor
import java.util.concurrent.Executors
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicInteger
import java.util.concurrent.atomic.AtomicReference

class NativeQueuedHttpTransportTest {
    @Test fun LogoutCancelsProductionQueuedRequestAndWipesRetainedBearerAndBody() {
        val executor = ControlledExecutor()
        val networkCalls = AtomicInteger()
        val handedOff = AtomicReference<Pair<GatedRequest, String>?>()
        val transport = NativeQueuedHttpTransport(executor) { request, bearer ->
            networkCalls.incrementAndGet()
            handedOff.set(request to bearer)
            NativeApiResponse(200, mapOf("content-type" to "application/json"), "{}")
        }
        val source = Credentials()
        val coordinator = NativeRequestCoordinator(
            NativeRequestGate("https://api.example.test"),
            source,
            transport,
            onSessionInvalidated = { transport.invalidateQueued() },
        )
        val context = coordinator.activateContext(1, "user-1", "space-a")
        val permit = coordinator.prepare(context, "queued", NativeApiRequest(
            "https://api.example.test/api/finance/operations", "POST",
            mapOf("Content-Type" to "application/json", "X-Workspace-Id" to "space-a"),
            "{\"amount\":\"12.30\"}",
        ))
        val dispatchWorker = Executors.newSingleThreadExecutor()
        val failure = AtomicReference<Throwable?>()
        try {
            val dispatch = dispatchWorker.submit { runCatching { coordinator.dispatch(permit) }.onFailure(failure::set) }
            assertTrue(executor.enqueued.await(2, TimeUnit.SECONDS))
            assertEquals(1, transport.retainedQueuedPayloadCount())

            val logoutResult = coordinator.invalidate {
                source.clear()
                "logout-cleared"
            }
            assertEquals("logout-cleared", logoutResult)
            assertNull(source.credentials())
            assertEquals(0, transport.retainedQueuedPayloadCount())
            assertEquals(0, networkCalls.get())

            executor.runNext()
            dispatch.get(2, TimeUnit.SECONDS)
            assertTrue(failure.get() is SecurityException || failure.get() is java.util.concurrent.CancellationException)
            assertEquals(0, networkCalls.get())
            assertNull(handedOff.get())
        } finally {
            dispatchWorker.shutdownNow()
        }
    }

    private class ControlledExecutor : Executor {
        private val tasks = java.util.concurrent.LinkedBlockingQueue<Runnable>()
        val enqueued = CountDownLatch(1)
        override fun execute(command: Runnable) { tasks.add(command); enqueued.countDown() }
        fun runNext() { tasks.poll(2, TimeUnit.SECONDS)?.run() ?: error("No queued task") }
    }

    private class Credentials : NativeCredentialSource {
        private var userId: String? = "user-1"
        private var value: NativeCredentials? = NativeCredentials("bearer-secret", "refresh-secret", Long.MAX_VALUE)
        override fun trustedUserId() = userId
        override fun credentials() = value
        override fun refresh(current: NativeCredentials) = current
        override fun commitRefresh(value: NativeCredentials): Boolean { this.value = value; return true }
        fun clear() { userId = null; value = null }
    }
}
