package com.enigmabudget.dev

import okhttp3.Dispatcher
import okhttp3.OkHttpClient
import okhttp3.Request
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.BufferedReader
import java.io.Closeable
import java.io.InputStreamReader
import java.net.InetAddress
import java.net.ServerSocket
import java.net.Socket
import java.net.SocketTimeoutException
import java.net.URI
import java.net.CookieHandler
import java.util.concurrent.CancellationException
import java.util.concurrent.CopyOnWriteArrayList
import java.util.concurrent.CountDownLatch
import java.util.concurrent.ExecutionException
import java.util.concurrent.Executors
import java.util.concurrent.TimeUnit

class OkHttpNativeHttpClientTest {
    @Test fun PrivateAuthUsesNoProcessCookiesAndNeverReplaysOneShotWrites() {
        val priorCookieHandler = CookieHandler.getDefault()
        CookieHandler.setDefault(object : CookieHandler() {
            override fun get(uri: URI, requestHeaders: Map<String, List<String>>): Map<String, List<String>> = mapOf("Cookie" to listOf("ambient-sentinel=secret"))
            override fun put(uri: URI, responseHeaders: Map<String, List<String>>) = Unit
        })
        try {
            ScriptedServer { _, _ -> response(503, "Retry-After: 0\r\n") }.use { server ->
                val result = OkHttpPrivateAuthClient().execute(
                    server.url("/api/auth/native/refresh"), "POST",
                    mapOf("accept" to "application/json", "content-type" to "application/json", "authorization" to "Bearer private-secret"),
                    "{\"refreshToken\":\"one-shot-secret\"}", 2_000,
                )
                assertEquals(503, result.status)
                assertEquals(listOf("/api/auth/native/refresh"), server.paths.toList())
                assertFalse(server.headers.any { it.contains("ambient-sentinel") || it.contains("Cookie:", true) })
            }
            ScriptedServer { _, _ -> null }.use { server ->
                org.junit.Assert.assertThrows(java.io.IOException::class.java) {
                    OkHttpPrivateAuthClient().execute(
                        server.url("/api/auth/native/exchange"), "POST",
                        mapOf("accept" to "application/json", "content-type" to "application/json"),
                        "{\"idToken\":\"one-shot-secret\"}", 2_000,
                    )
                }
                Thread.sleep(100)
                assertEquals(listOf("/api/auth/native/exchange"), server.paths.toList())
            }
        } finally {
            CookieHandler.setDefault(priorCookieHandler)
        }
    }

    @Test fun DoesNotReplay503RetryAfterZeroOrSocketFailure() {
        ScriptedServer { _, _ -> response(503, "Retry-After: 0\r\n") }.use { server ->
            val client = newNativeOkHttpClient()
            try {
                val transport = NativeAsyncHttpTransport(OkHttpNativeHttpClient(client))
                val result = transport.dispatch(request(server.url("/api/read")), "bearer-secret").get(4, TimeUnit.SECONDS)
                Thread.sleep(150)
                assertEquals(503, result.status)
                assertEquals("0", result.headers["retry-after"])
                assertFalse(result.headers.containsKey("x-enigmabudget-internal-retry-after"))
                assertEquals(listOf("/api/read"), server.paths.toList())
            } finally {
                client.dispatcher.executorService.shutdownNow()
            }
        }

        ScriptedServer { _, _ -> null }.use { server ->
            val client = newNativeOkHttpClient()
            try {
                val transport = NativeAsyncHttpTransport(OkHttpNativeHttpClient(client))
                val call = transport.dispatch(GatedRequest(
                    URI(server.url("/api/mutation")), "POST",
                    mapOf("accept" to "application/json", "content-type" to "application/json"),
                    "{\"amount\":\"12.34\"}",
                ), "bearer-secret")
                org.junit.Assert.assertThrows(ExecutionException::class.java) { call.get(4, TimeUnit.SECONDS) }
                Thread.sleep(150)
                assertEquals(listOf("/api/mutation"), server.paths.toList())
            } finally {
                client.dispatcher.executorService.shutdownNow()
            }
        }
    }

    @Test fun LogoutCancelsAnOkHttpAcceptedCallStillQueuedBeforeSocketWrite() {
        ScriptedServer { path, server ->
            if (path == "/blocker") {
                server.awaitRelease()
                response(200)
            } else response(200)
        }.use { server ->
            val dispatcher = Dispatcher().apply { maxRequests = 1; maxRequestsPerHost = 1 }
            val client = newNativeOkHttpClient().newBuilder().dispatcher(dispatcher).build()
            val blocker = client.newCall(Request.Builder().url(server.url("/blocker")).build())
            blocker.enqueue(object : okhttp3.Callback {
                override fun onFailure(call: okhttp3.Call, error: java.io.IOException) = Unit
                override fun onResponse(call: okhttp3.Call, response: okhttp3.Response) { response.close() }
            })
            assertTrue(server.blockerAccepted.await(2, TimeUnit.SECONDS))

            val nativeClient = OkHttpNativeHttpClient(client)
            val transport = NativeAsyncHttpTransport(nativeClient)
            val source = Credentials()
            val coordinator = NativeRequestCoordinator(
                NativeRequestGate(server.url(""), allowDebugLocalhost = true), source, transport,
                onRequestsInvalidated = transport::cancelPending,
            )
            val context = coordinator.activateContext(1, "user-1", null)
            val permit = coordinator.prepare(context, "queued-to-okhttp", NativeApiRequest(
                server.url("/api/protected"), "GET", emptyMap(),
            ))
            val worker = Executors.newSingleThreadExecutor()
            try {
                val dispatch = worker.submit { coordinator.dispatch(permit) }
                val queued = waitFor(2_000) {
                    client.dispatcher.queuedCalls().any { it.request().url.encodedPath == "/api/protected" }
                }
                assertTrue("protected call reached OkHttp's own queue", queued)

                coordinator.invalidate { source.clear(); Unit }
                blocker.cancel()
                server.releaseBlocker()
                val dispatchError = org.junit.Assert.assertThrows(ExecutionException::class.java) { dispatch.get(2, TimeUnit.SECONDS) }
                assertTrue(dispatchError.cause is CancellationException)
                Thread.sleep(150)

                assertTrue(server.paths.contains("/blocker"))
                assertFalse("canceled protected call must not reach the socket", server.paths.contains("/api/protected"))
            } finally {
                server.releaseBlocker()
                blocker.cancel()
                worker.shutdownNow()
                client.dispatcher.executorService.shutdownNow()
            }
        }
    }

    private fun request(url: String) = GatedRequest(URI(url), "GET", mapOf("accept" to "application/json"), null)

    private fun response(status: Int, extraHeaders: String = ""): ByteArray =
        "HTTP/1.1 $status ${if (status == 503) "Service Unavailable" else "OK"}\r\nContent-Type: application/json\r\n${extraHeaders}Content-Length: 2\r\nConnection: close\r\n\r\n{}".toByteArray()

    private fun waitFor(timeoutMillis: Long, condition: () -> Boolean): Boolean {
        val start = System.nanoTime()
        while (TimeUnit.NANOSECONDS.toMillis(System.nanoTime() - start) < timeoutMillis) {
            if (condition()) return true
            Thread.sleep(5)
        }
        return condition()
    }

    private class ScriptedServer(private val responder: (String, ScriptedServer) -> ByteArray?) : Closeable {
        private val serverSocket = ServerSocket(0, 32, InetAddress.getByName("127.0.0.1"))
        private val stop = CountDownLatch(1)
        val paths = CopyOnWriteArrayList<String>()
        val headers = CopyOnWriteArrayList<String>()
        val blockerAccepted = CountDownLatch(1)
        private val releaseBlocker = CountDownLatch(1)
        private val thread = Thread(::serve, "native-http-test-server").apply { isDaemon = true; start() }

        fun url(path: String) = "http://127.0.0.1:${serverSocket.localPort}$path"
        fun awaitRelease() { blockerAccepted.countDown(); releaseBlocker.await(3, TimeUnit.SECONDS) }
        fun releaseBlocker() { releaseBlocker.countDown() }

        private fun serve() {
            serverSocket.soTimeout = 100
            while (stop.count == 1L) {
                val socket = try { serverSocket.accept() } catch (_: SocketTimeoutException) { continue } catch (_: Exception) { break }
                socket.use { client ->
                    client.soTimeout = 3_000
                    val reader = BufferedReader(InputStreamReader(client.getInputStream(), Charsets.US_ASCII))
                    val firstLine = reader.readLine() ?: return@use
                    val path = firstLine.split(' ').getOrNull(1).orEmpty()
                    while (true) {
                        val line = reader.readLine() ?: break
                        if (line.isEmpty()) break
                        headers += line
                    }
                    paths += path
                    val bytes = responder(path, this) ?: return@use
                    try { client.getOutputStream().apply { write(bytes); flush() } } catch (_: Exception) { }
                }
            }
        }

        override fun close() {
            stop.countDown()
            releaseBlocker.countDown()
            serverSocket.close()
            thread.join(1_000)
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
