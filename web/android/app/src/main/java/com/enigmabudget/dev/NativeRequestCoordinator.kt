package com.enigmabudget.dev

import java.util.UUID
import java.util.concurrent.CompletableFuture
import java.util.concurrent.ExecutionException

data class NativeCredentials(val accessToken: String, val refreshToken: String, val accessExpiresAtMillis: Long)
data class NativeRequestContext(val id: String, val angularEpoch: Long, val userId: String, val workspaceId: String?, val sessionGeneration: Long, val contextGeneration: Long)
data class NativePermit(val value: String)
data class NativeDispatchResult(val response: NativeApiResponse, val invalidTokenChallenge: Boolean)

class NativeHttpFailure(val status: Int) : RuntimeException("Native API request failed.")

interface NativeCredentialSource {
    fun trustedUserId(): String?
    fun credentials(): NativeCredentials?
    /** May block on network; the coordinator never holds its state lock while this runs. */
    fun refresh(current: NativeCredentials): NativeCredentials
    /** Called under the coordinator state lock after a generation check. */
    fun commitRefresh(value: NativeCredentials): Boolean
}

fun interface NativeHttpTransport {
    /** Must start the handoff and return immediately; never wait for the response here. */
    fun dispatch(request: GatedRequest, bearer: String): CompletableFuture<NativeApiResponse>
}

/**
 * Serializes context/permit state and the instant a transport handoff begins, while allowing
 * network waits and refresh I/O to run outside the state lock. Stale completions are discarded.
 */
class NativeRequestCoordinator(
    private val gate: NativeRequestGate,
    private val credentialSource: NativeCredentialSource,
    private val transport: NativeHttpTransport,
    private val clock: () -> Long = System::currentTimeMillis,
    private val permitTtlMillis: Long = 30_000,
    private val onSessionInvalidated: () -> Unit = {},
) {
    private data class Pending(
        val requestId: String,
        val request: GatedRequest,
        val contextId: String,
        val userId: String,
        val workspaceId: String?,
        val sessionGeneration: Long,
        val contextGeneration: Long,
        val expiresAt: Long,
        var retries: Int = 0,
    )
    private data class Marker(val permit: String, val requestId: String, val contextId: String)
    private data class RefreshFlight(val generation: Long, val future: CompletableFuture<NativeCredentials>)

    private val stateLock = Any()
    private val permits = linkedMapOf<String, Pending>()
    private val retryPending = linkedMapOf<String, Pending>()
    private val contexts = linkedMapOf<String, NativeRequestContext>()
    private val retryMarkers = linkedMapOf<String, Marker>()
    private var currentCredentials: NativeCredentials? = null
    private var refreshFlight: RefreshFlight? = null
    private var sessionGeneration = 1L
    private var contextGeneration = 0L
    private var latestEpoch = Long.MIN_VALUE

    fun activateContext(angularEpoch: Long, userId: String, workspaceId: String?): NativeRequestContext = synchronized(stateLock) {
        val trusted = credentialSource.trustedUserId() ?: throw SecurityException("No authenticated native session.")
        if (trusted != userId) throw SecurityException("Requested user does not match the native session.")
        if (angularEpoch < latestEpoch) throw SecurityException("Out-of-order request context.")
        latestEpoch = angularEpoch
        contextGeneration++
        permits.clear(); retryPending.clear(); retryMarkers.clear(); contexts.clear()
        val context = NativeRequestContext(UUID.randomUUID().toString(), angularEpoch, trusted, workspaceId, sessionGeneration, contextGeneration)
        contexts[context.id] = context
        context
    }

    fun prepare(context: NativeRequestContext, requestId: String, request: NativeApiRequest): NativePermit {
        val active = synchronized(stateLock) { validateContext(context) }
        val gated = gate.validate(request)
        val requestWorkspace = gated.headers.entries.firstOrNull { it.key.equals("X-Workspace-Id", true) }?.value
        if (requestWorkspace != null && requestWorkspace != active.workspaceId) throw SecurityException("Workspace does not match the active context.")
        ensureFresh(active.sessionGeneration, force = false)
        return synchronized(stateLock) {
            val stillActive = validateContext(active)
            val permit = UUID.randomUUID().toString()
            permits[permit] = Pending(requestId, gated, stillActive.id, stillActive.userId, stillActive.workspaceId,
                stillActive.sessionGeneration, stillActive.contextGeneration, clock() + permitTtlMillis)
            NativePermit(permit)
        }
    }

    fun dispatch(permit: NativePermit): NativeDispatchResult {
        val pending = synchronized(stateLock) {
            val value = permits.remove(permit.value) ?: throw SecurityException("Invalid or already used request permit.")
            validatePending(value)
            value
        }
        return try {
            val credentials = ensureFresh(pending.sessionGeneration, force = false)
            val responseFuture = synchronized(stateLock) {
                validatePending(pending)
                // The transport contract schedules the network action and returns without waiting.
                transport.dispatch(pending.request, credentials.accessToken)
            }
            val response = await(responseFuture)
            val safeResponse = gate.validateResponse(pending.request.url, response)
            synchronized(stateLock) {
                validatePending(pending)
                val invalidToken = safeResponse.status == 401 && safeResponse.headers.entries.any { (key, value) ->
                    key.equals("WWW-Authenticate", true) && Regex("Bearer\\s+error=\\\"invalid_token\\\"", RegexOption.IGNORE_CASE).containsMatchIn(value)
                }
                if (invalidToken && pending.retries == 0) {
                    retryMarkers[permit.value] = Marker(permit.value, pending.requestId, pending.contextId)
                    retryPending[permit.value] = pending
                } else clearPending(pending)
                NativeDispatchResult(safeResponse, invalidToken)
            }
        } catch (error: Throwable) {
            synchronized(stateLock) { clearPending(pending) }
            throw unwrap(error)
        }
    }

    fun prepareUnauthorizedRetry(context: NativeRequestContext, requestId: String, priorPermit: NativePermit): NativePermit {
        val pending = synchronized(stateLock) {
            val active = validateContext(context)
            val marker = retryMarkers.remove(priorPermit.value) ?: throw SecurityException("No authentication middleware rejection was recorded.")
            val saved = retryPending.remove(priorPermit.value) ?: throw SecurityException("Retry request is no longer pending.")
            if (marker.permit != priorPermit.value || marker.requestId != requestId || marker.contextId != active.id || saved.requestId != requestId || saved.contextId != active.id || saved.retries != 0) {
                clearPending(saved)
                throw SecurityException("Retry does not match the rejected request.")
            }
            saved
        }
        try {
            ensureFresh(pending.sessionGeneration, force = true)
            return synchronized(stateLock) {
                validatePending(pending)
                pending.retries = 1
                val next = UUID.randomUUID().toString()
                permits[next] = pending.copy(expiresAt = clock() + permitTtlMillis, retries = 1)
                NativePermit(next)
            }
        } catch (error: Throwable) {
            synchronized(stateLock) { clearPending(pending) }
            throw unwrap(error)
        }
    }

    /** Invalidates native state and performs the caller's local key/token clearing atomically. */
    fun <T> invalidate(clearNativeState: () -> T): T = synchronized(stateLock) {
        sessionGeneration++
        contextGeneration++
        onSessionInvalidated()
        permits.clear(); retryPending.clear(); retryMarkers.clear(); contexts.clear()
        currentCredentials = null
        refreshFlight?.future?.completeExceptionally(SecurityException("Native session changed during refresh."))
        refreshFlight = null
        clearNativeState()
    }

    fun invalidate() { invalidate { Unit } }

    fun currentSessionGeneration(): Long = synchronized(stateLock) { sessionGeneration }

    /** Installs a sign-in result only if logout/account replacement did not overtake its I/O. */
    fun installSession(generation: Long, credentials: NativeCredentials, commit: () -> Boolean): Boolean = synchronized(stateLock) {
        if (generation != sessionGeneration || !commit()) return@synchronized false
        currentCredentials = credentials
        true
    }

    fun updateSessionInfo(generation: Long, update: () -> Unit): Boolean = synchronized(stateLock) {
        if (generation != sessionGeneration || currentCredentials == null) return@synchronized false
        update()
        true
    }

    fun ensureSessionCredentialsFresh(): NativeCredentials = ensureFresh(synchronized(stateLock) { sessionGeneration }, force = false)

    private fun ensureFresh(expectedGeneration: Long, force: Boolean): NativeCredentials {
        val (flight, leader, current) = synchronized(stateLock) {
            if (sessionGeneration != expectedGeneration) throw SecurityException("Native session changed during refresh.")
            val current = currentCredentials ?: credentialSource.credentials()?.also { currentCredentials = it }
                ?: throw SecurityException("No native credentials are available.")
            if (!force && current.accessExpiresAtMillis > clock() + 5_000) return current
            val existing = refreshFlight?.takeIf { it.generation == expectedGeneration }
            if (existing != null) Triple(existing, false, current)
            else {
                val created = RefreshFlight(expectedGeneration, CompletableFuture())
                refreshFlight = created
                Triple(created, true, current)
            }
        }
        if (leader) {
            try {
                val refreshed = credentialSource.refresh(current)
                synchronized(stateLock) {
                    if (sessionGeneration != expectedGeneration || refreshFlight !== flight || !credentialSource.commitRefresh(refreshed)) {
                        throw SecurityException("Native session changed during refresh.")
                    }
                    currentCredentials = refreshed
                    refreshFlight = null
                }
                flight.future.complete(refreshed)
            } catch (error: Throwable) {
                synchronized(stateLock) { if (refreshFlight === flight) refreshFlight = null }
                flight.future.completeExceptionally(error)
            }
        }
        return await(flight.future)
    }

    private fun validateContext(context: NativeRequestContext): NativeRequestContext {
        val active = contexts[context.id] ?: throw SecurityException("Request context is no longer active.")
        if (active != context || active.sessionGeneration != sessionGeneration || active.contextGeneration != contextGeneration) throw SecurityException("Request context is stale.")
        if (credentialSource.trustedUserId() != active.userId) throw SecurityException("Native session identity changed.")
        return active
    }

    private fun validatePending(pending: Pending) {
        val context = contexts[pending.contextId]
        if (context == null || sessionGeneration != pending.sessionGeneration || contextGeneration != pending.contextGeneration ||
            context.userId != pending.userId || context.workspaceId != pending.workspaceId || clock() > pending.expiresAt) {
            clearPending(pending)
            throw SecurityException("Request permit expired or was invalidated.")
        }
        if (credentialSource.trustedUserId() != pending.userId) {
            clearPending(pending)
            throw SecurityException("Native session identity changed.")
        }
    }

    private fun clearPending(pending: Pending) {
        retryMarkers.entries.removeAll { it.value.requestId == pending.requestId }
        retryPending.entries.removeAll { it.value.requestId == pending.requestId }
    }

    private fun <T> await(future: CompletableFuture<T>): T = try { future.get() } catch (error: ExecutionException) { throw unwrap(error.cause ?: error) }

    private fun unwrap(error: Throwable): Throwable = if (error is ExecutionException && error.cause != null) error.cause!! else error
}
