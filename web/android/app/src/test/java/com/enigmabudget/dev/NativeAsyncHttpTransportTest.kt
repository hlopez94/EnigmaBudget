package com.enigmabudget.dev

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertThrows
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.IOException
import java.util.concurrent.CancellationException
import java.util.concurrent.CountDownLatch
import java.util.concurrent.ExecutionException
import java.util.concurrent.Executors
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicReference
import java.util.concurrent.CopyOnWriteArrayList

class NativeAsyncHttpTransportTest {
    @Test fun ClientEnqueueIsSerializedWithLogoutAndAcceptedCallIsCanceled() {
        val enqueueEntered = CountDownLatch(1)
        val releaseEnqueue = CountDownLatch(1)
        val logoutStarted = CountDownLatch(1)
        val events = CopyOnWriteArrayList<String>()
        val callback = AtomicReference<NativeAsyncHttpCallback?>()
        val client = NativeAsyncHttpClient { _, bearer ->
            assertEquals("bearer-secret", bearer)
            object : NativeAsyncHttpCall {
                override fun enqueue(value: NativeAsyncHttpCallback) {
                    callback.set(value)
                    enqueueEntered.countDown()
                    check(releaseEnqueue.await(2, TimeUnit.SECONDS))
                    events += "http-client-accepted"
                }
                override fun cancel() {
                    events += "http-client-canceled"
                    callback.get()?.onFailure(IOException("Canceled"))
                }
            }
        }
        val transport = NativeAsyncHttpTransport(client)
        val source = Credentials()
        val coordinator = NativeRequestCoordinator(
            NativeRequestGate("https://api.example.test"), source, transport,
            onRequestsInvalidated = transport::cancelPending,
        )
        val context = coordinator.activateContext(1, "user-1", null)
        val permit = coordinator.prepare(context, "logout-gap", NativeApiRequest("https://api.example.test/api/me", "GET", emptyMap()))
        val dispatchWorker = Executors.newSingleThreadExecutor()
        val logoutWorker = Executors.newSingleThreadExecutor()
        try {
            val dispatch = dispatchWorker.submit { coordinator.dispatch(permit) }
            assertTrue(enqueueEntered.await(2, TimeUnit.SECONDS))
            val logout = logoutWorker.submit {
                logoutStarted.countDown()
                coordinator.invalidate { source.clear(); events += "local-session-cleared" }
                events += "logout-returned"
            }
            assertTrue(logoutStarted.await(2, TimeUnit.SECONDS))
            assertFalse("logout must wait only for nonblocking client acceptance", logout.isDone)

            releaseEnqueue.countDown()
            logout.get(2, TimeUnit.SECONDS)
            assertNull(source.credentials())
            assertTrue(events.indexOf("http-client-accepted") < events.indexOf("http-client-canceled"))
            assertTrue(events.indexOf("http-client-canceled") < events.indexOf("local-session-cleared"))
            assertEquals("logout-returned", events.last())
            val dispatchError = assertThrows(ExecutionException::class.java) { dispatch.get(2, TimeUnit.SECONDS) }
            assertTrue(dispatchError.cause is CancellationException)
        } finally {
            releaseEnqueue.countDown()
            dispatchWorker.shutdownNow()
            logoutWorker.shutdownNow()
        }
    }

    @Test fun WorkspaceContextChangeCancelsAcceptedOldWorkspaceCallAndKeepsSessionCredentials() {
        val callback = AtomicReference<NativeAsyncHttpCallback?>()
        val canceled = CountDownLatch(1)
        val client = NativeAsyncHttpClient { _, _ ->
            object : NativeAsyncHttpCall {
                override fun enqueue(value: NativeAsyncHttpCallback) { callback.set(value) }
                override fun cancel() { canceled.countDown(); callback.get()?.onFailure(IOException("Canceled")) }
            }
        }
        val transport = NativeAsyncHttpTransport(client)
        val source = Credentials()
        val coordinator = NativeRequestCoordinator(
            NativeRequestGate("https://api.example.test"), source, transport,
            onRequestsInvalidated = transport::cancelPending,
        )
        val oldContext = coordinator.activateContext(1, "user-1", "workspace-old")
        val permit = coordinator.prepare(oldContext, "old-space", NativeApiRequest(
            "https://api.example.test/api/finance/operations", "GET",
            mapOf("X-Workspace-Id" to "workspace-old"),
        ))
        val dispatchWorker = Executors.newSingleThreadExecutor()
        try {
            val dispatch = dispatchWorker.submit { coordinator.dispatch(permit) }
            var elapsed = 0
            while (callback.get() == null && elapsed < 200) { Thread.sleep(5); elapsed += 5 }
            assertNotNull(callback.get())

            val nextContext = coordinator.activateContext(2, "user-1", "workspace-new")
            assertEquals("workspace-new", nextContext.workspaceId)
            assertTrue(canceled.await(2, TimeUnit.SECONDS))
            assertNotNull(source.credentials())
            val dispatchError = assertThrows(ExecutionException::class.java) { dispatch.get(2, TimeUnit.SECONDS) }
            assertTrue(dispatchError.cause is CancellationException)
        } finally {
            dispatchWorker.shutdownNow()
        }
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
