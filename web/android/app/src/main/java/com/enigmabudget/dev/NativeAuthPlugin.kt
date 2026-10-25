package com.enigmabudget.dev

import android.content.Context
import android.net.Uri
import androidx.credentials.CredentialManager
import androidx.credentials.CustomCredential
import androidx.credentials.GetCredentialRequest
import androidx.credentials.GetCredentialResponse
import androidx.credentials.exceptions.GetCredentialException
import com.google.android.libraries.identity.googleid.GetGoogleIdOption
import com.google.android.libraries.identity.googleid.GetSignInWithGoogleOption
import com.google.android.libraries.identity.googleid.GoogleIdTokenCredential
import com.getcapacitor.JSObject
import com.getcapacitor.Plugin
import com.getcapacitor.PluginCall
import com.getcapacitor.PluginMethod
import com.getcapacitor.annotation.CapacitorPlugin
import org.json.JSONObject
import java.net.URI
import java.util.UUID
import java.util.concurrent.ConcurrentHashMap
import java.util.concurrent.CompletableFuture
import java.util.concurrent.Executors
import java.util.concurrent.ThreadPoolExecutor
import java.util.concurrent.TimeUnit

@CapacitorPlugin(name = "NativeAuth")
class NativeAuthPlugin : Plugin() {
    private val executor = boundedExecutor("native-auth")
    private val protectedTransport by lazy { NativeAsyncHttpTransport(OkHttpNativeHttpClient()) }
    private val privateAuthClient by lazy { OkHttpPrivateAuthClient() }
    private val logoutExecutor = ThreadPoolExecutor(1, 1, 0L, TimeUnit.MILLISECONDS, java.util.concurrent.SynchronousQueue(), { runnable ->
        Thread(runnable, "native-logout-${UUID.randomUUID()}").apply { isDaemon = true }
    }, ThreadPoolExecutor.AbortPolicy())
    private val cleanupExecutor = Executors.newSingleThreadScheduledExecutor()
    private lateinit var sessionStore: KeystoreSessionStore
    @Volatile private var accessToken: String? = null
    @Volatile private var userId: String? = null
    @Volatile private var displayName: String? = null
    @Volatile private var expiresAt: Long = 0L
    private val gate by lazy { NativeRequestGate(requireApiBase(), BuildConfig.DEBUG && BuildConfig.ALLOW_LOCAL_HTTP) }
    private val coordinatorDelegate = lazy {
        NativeRequestCoordinator(gate, object : NativeCredentialSource {
            override fun trustedUserId(): String? = userId
            override fun credentials(): NativeCredentials? {
                val refresh = sessionStore.read() ?: return null
                return NativeCredentials(accessToken.orEmpty(), refresh, expiresAt)
            }
            override fun refresh(current: NativeCredentials): NativeCredentials = try { refreshNative(current) }
            catch (error: Exception) { clearCredentials(); throw error }
            override fun commitRefresh(value: NativeCredentials): Boolean {
                sessionStore.store(value.refreshToken)
                accessToken = value.accessToken
                expiresAt = value.accessExpiresAtMillis
                return true
            }
        }, protectedTransport, onRequestsInvalidated = protectedTransport::cancelPending)
    }
    private val coordinator by coordinatorDelegate
    private val logoutResults = ConcurrentHashMap<String, CompletableFuture<Boolean>>()

    override fun load() {
        sessionStore = KeystoreSessionStore(AndroidKeystoreSessionKeyProvider(), SharedPreferencesEncryptedValueStore(context))
    }

    @PluginMethod
    fun authOptions(call: PluginCall) = execute(call) {
        val options = readAuthOptions()
        call.resolve(JSObject().put("googleConfigured", options.googleConfigured)
            .put("developmentLoginEnabled", options.developmentLoginEnabled))
    }

    @PluginMethod
    fun invitationHost(call: PluginCall) = execute(call) {
        val host = BuildConfig.APP_LINKS_HOST.takeIf { it.isNotBlank() }
        call.resolve(JSObject().put("host", host))
    }

    @PluginMethod
    fun sessionInfo(call: PluginCall) = execute(call) {
        if (sessionStore.read() == null) {
            clearMemorySession()
            call.resolve(sessionObject(false, null, null)); return@execute
        }
        if (BuildConfig.API_BASE_URL.isBlank()) {
            accessToken = null; userId = null; displayName = null; expiresAt = 0L
            call.resolve(sessionObject(false, null, null)); return@execute
        }
        val generation = coordinator.currentSessionGeneration()
        try {
            if (accessToken == null || expiresAt <= System.currentTimeMillis()) coordinator.ensureSessionCredentialsFresh()
            val response = authRequest("GET", "/api/auth/native/session", null, accessToken)
            if (response.status == 401) throw NativeHttpFailure(401)
            if (response.status !in 200..299) throw NativeHttpFailure(response.status)
            val json = JSONObject(response.body)
            val serverUserId = json.optString("userId").takeIf { it.isNotBlank() && it != "null" }
            if (!json.optBoolean("authenticated") || serverUserId == null) throw NativeHttpFailure(401)
            if (userId != null && userId != serverUserId) throw SecurityException("Native session identity changed.")
            val serverDisplayName = json.optString("displayName").takeIf { it.isNotBlank() && it != "null" }
            if (!coordinator.updateSessionInfo(generation) { userId = serverUserId; displayName = serverDisplayName }) {
                throw SecurityException("Native session changed during restore.")
            }
            call.resolve(sessionObject(true, userId, displayName))
        } catch (error: Exception) {
            if (coordinator.currentSessionGeneration() != generation) {
                call.resolve(sessionObject(false, null, null)); return@execute
            }
            if (error is NativeHttpFailure && error.status == 401) clearCredentials() else clearMemorySession()
            call.resolve(sessionObject(false, null, null))
        }
    }

    @PluginMethod
    fun signInGoogle(call: PluginCall) = execute(call) {
        if (BuildConfig.GOOGLE_SERVER_CLIENT_ID.isBlank()) throw IllegalStateException("Google sign-in is not configured.")
        val startingGeneration = coordinator.currentSessionGeneration()
        val options = readAuthOptions()
        if (!options.googleConfigured) throw IllegalStateException("Google sign-in is not configured on the API.")
        val challenge = JSONObject(authRequest("POST", "/api/auth/native/challenge", JSONObject().put("platform", "android")).body)
        if (coordinator.currentSessionGeneration() != startingGeneration) throw SecurityException("Native session changed during sign-in.")
        val generation = beginAccountReplacement()
        val idToken = getGoogleIdToken(challenge.getString("nonce"))
        val exchange = JSONObject().put("challengeId", challenge.getString("challengeId"))
            .put("idToken", idToken).put("deviceName", android.os.Build.MODEL)
        installTokens(authRequest("POST", "/api/auth/native/exchange", exchange), generation)
        resolveVoid(call)
    }

    @PluginMethod
    fun signInDevelopment(call: PluginCall) = execute(call) {
        val startingGeneration = coordinator.currentSessionGeneration()
        val options = readAuthOptions()
        if (!BuildConfig.DEBUG || !options.developmentLoginEnabled) throw IllegalStateException("El inicio de desarrollo no está habilitado.")
        if (coordinator.currentSessionGeneration() != startingGeneration) throw SecurityException("Native session changed during sign-in.")
        val generation = beginAccountReplacement()
        val body = JSONObject().put("deviceName", android.os.Build.MODEL)
        call.getString("key")?.let { body.put("key", it) }
        installTokens(authRequest("POST", "/api/auth/native/development", body), generation)
        resolveVoid(call)
    }

    @PluginMethod
    fun activateRequestContext(call: PluginCall) = execute(call) {
        val context = coordinator.activateContext(call.getLong("angularEpoch") ?: -1L, call.getString("userId") ?: "", call.getString("workspaceId"))
        contexts[context.id] = context
        call.resolve(JSObject().put("value", context.id))
    }

    @PluginMethod
    fun prepareRequest(call: PluginCall) = execute(call) {
        val requestJson = call.getObject("request") ?: throw IllegalArgumentException("Request is required.")
        val rawUrl = requestJson.getString("url")
        val method = requestJson.getString("method") ?: throw IllegalArgumentException("Request method is required.")
        val absoluteUrl = URI(requireApiBase()).resolve(rawUrl).toString()
        val body = if (requestJson.has("body")) requestJson.getString("body") else null
        val request = NativeApiRequest(absoluteUrl, method, stringMap(requestJson.getJSONObject("headers")), body)
        val context = contexts[call.getString("context") ?: ""] ?: throw SecurityException("Context is not active.")
        val permit = coordinator.prepare(context, call.getString("requestId") ?: "", request)
        call.resolve(JSObject().put("value", permit.value))
    }

    @PluginMethod
    fun dispatchRequest(call: PluginCall) = execute(call) {
        val result = coordinator.dispatch(NativePermit(call.getString("permit") ?: ""))
        call.resolve(responseObject(result.response))
    }

    @PluginMethod
    fun prepareUnauthorizedRetry(call: PluginCall) = execute(call) {
        val active = contexts[call.getString("context") ?: ""] ?: throw SecurityException("Context is not active.")
        val prior = NativePermit(call.getString("priorPermit") ?: "")
        val next = coordinator.prepareUnauthorizedRetry(active, call.getString("requestId") ?: "", prior)
        call.resolve(JSObject().put("value", next.value))
    }

    @PluginMethod
    fun beginLogout(call: PluginCall) {
        val refresh = if (coordinatorDelegate.isInitialized()) coordinator.invalidate {
            val value = sessionStore.read()
            clearLocalStateWithoutCoordinator()
            sessionStore.clear()
            value
        } else sessionStore.read().also { clearLocalStateWithoutCoordinator(); sessionStore.clear() }
        val id = UUID.randomUUID().toString()
        val result = try {
            if (refresh == null) CompletableFuture.completedFuture(false) else CompletableFuture.supplyAsync({
                try { authRequest("POST", "/api/auth/native/logout", JSONObject().put("refreshToken", refresh), timeoutMillis = 1_400).status in 200..299 }
                catch (_: Exception) { false }
            }, logoutExecutor)
        } catch (_: java.util.concurrent.RejectedExecutionException) { CompletableFuture.completedFuture(false) }
        logoutResults[id] = result
        cleanupExecutor.schedule({ logoutResults.remove(id, result) }, 30, TimeUnit.SECONDS)
        call.resolve(JSObject().put("value", id))
    }

    @PluginMethod
    fun logoutResult(call: PluginCall) = execute(call) {
        val id = call.getString("attempt") ?: ""
        val result = logoutResults[id]
        val revoked = awaitLogoutRevocation(result)
        if (result != null) logoutResults.remove(id, result)
        call.resolve(JSObject().put("serverRevoked", revoked))
    }

    @PluginMethod
    fun clearLocalSession(call: PluginCall) = execute(call) { clearCredentials(); resolveVoid(call) }

    private fun execute(call: PluginCall, block: () -> Unit) {
        try { executor.execute { try { block() } catch (_: Exception) { call.reject("No se pudo completar la operación de autenticación nativa.") } } }
        catch (_: java.util.concurrent.RejectedExecutionException) { call.reject("La operación nativa está ocupada. Volvé a intentar.") }
    }

    private fun refreshNative(current: NativeCredentials): NativeCredentials {
        val response = authRequest("POST", "/api/auth/native/refresh", JSONObject().put("refreshToken", current.refreshToken))
        if (response.status !in 200..299) throw NativeHttpFailure(response.status)
        val json = JSONObject(response.body)
        return NativeCredentials(json.getString("accessToken"), json.getString("refreshToken"), java.time.Instant.parse(json.getString("accessExpiresAt")).toEpochMilli())
    }

    private fun readAuthOptions(): NativeAuthOptions = NativeAuthOptionsProjection.project(authRequest("GET", "/api/auth/native/options", null))

    private fun installTokens(response: NativeApiResponse, generation: Long) {
        if (response.status !in 200..299) throw NativeHttpFailure(response.status)
        val json = JSONObject(response.body)
        val nextAccess = json.getString("accessToken")
        val nextRefresh = json.getString("refreshToken")
        val credentials = NativeCredentials(nextAccess, nextRefresh, java.time.Instant.parse(json.getString("accessExpiresAt")).toEpochMilli())
        if (coordinator.currentSessionGeneration() != generation) throw SecurityException("Sign-in completed after the native session changed.")
        val sessionResponse = authRequest("GET", "/api/auth/native/session", null, nextAccess)
        if (sessionResponse.status !in 200..299) throw NativeHttpFailure(sessionResponse.status)
        val session = JSONObject(sessionResponse.body)
        if (!session.optBoolean("authenticated")) throw SecurityException("Native session was not accepted.")
        val nextUserId = session.getString("userId")
        val nextDisplayName = session.optString("displayName").takeIf { it.isNotBlank() && it != "null" }
        val installed = coordinator.installSession(generation, credentials) {
            sessionStore.store(nextRefresh)
            accessToken = nextAccess
            expiresAt = credentials.accessExpiresAtMillis
            userId = nextUserId
            displayName = nextDisplayName
            contexts.clear()
            true
        }
        if (!installed) throw SecurityException("Sign-in completed after the native session changed.")
    }

    private fun authRequest(method: String, path: String, body: JSONObject?, bearer: String? = null, timeoutMillis: Int = 8_000): NativeApiResponse {
        val base = URI(requireApiBase())
        val uri = base.resolve(path)
        if (uri.host != base.host || uri.scheme != base.scheme) throw SecurityException("Invalid native API origin.")
        val headers = mutableMapOf("accept" to "application/json")
        if (body != null) headers["content-type"] = "application/json"
        if (bearer != null) headers["authorization"] = "Bearer $bearer"
        val response = privateAuthClient.execute(uri.toString(), method, headers, body?.toString(), timeoutMillis)
        if (response.status in 300..399) throw NativeHttpFailure(response.status)
        return response
    }

    private fun getGoogleIdToken(nonce: String): String {
        val manager = CredentialManager.create(context)
        val primary = GetGoogleIdOption.Builder().setServerClientId(BuildConfig.GOOGLE_SERVER_CLIENT_ID).setNonce(nonce).setFilterByAuthorizedAccounts(false).build()
        try {
            return credentialToken(getCredential(manager, GetCredentialRequest.Builder().addCredentialOption(primary).build()))
        } catch (_: Exception) {
            val fallback = GetSignInWithGoogleOption.Builder(BuildConfig.GOOGLE_SERVER_CLIENT_ID).setNonce(nonce).build()
            return credentialToken(getCredential(manager, GetCredentialRequest.Builder().addCredentialOption(fallback).build()))
        }
    }

    private fun getCredential(manager: CredentialManager, request: GetCredentialRequest): GetCredentialResponse {
        val future = java.util.concurrent.CompletableFuture<GetCredentialResponse>()
        activity.runOnUiThread {
            manager.getCredentialAsync(activity, request, null, context.mainExecutor, object : androidx.credentials.CredentialManagerCallback<GetCredentialResponse, GetCredentialException> {
                override fun onResult(result: GetCredentialResponse) { future.complete(result) }
                override fun onError(e: GetCredentialException) { future.completeExceptionally(e) }
            })
        }
        return future.get(90, TimeUnit.SECONDS)
    }

    private fun credentialToken(response: GetCredentialResponse): String {
        val credential = response.credential
        if (credential !is CustomCredential || credential.type != GoogleIdTokenCredential.TYPE_GOOGLE_ID_TOKEN_CREDENTIAL) throw SecurityException("Google credential was not an ID token.")
        return GoogleIdTokenCredential.createFrom(credential.data).idToken
    }

    private val contexts = ConcurrentHashMap<String, NativeRequestContext>()
    private fun requireApiBase(): String {
        val configured = BuildConfig.API_BASE_URL.takeIf { it.isNotBlank() }
            ?: throw IllegalStateException("La URL HTTPS de la API no está configurada para este entorno.")
        val parsed = try { URI(configured) } catch (_: Exception) { throw IllegalStateException("La URL de la API no es válida.") }
        val localOverride = BuildConfig.DEBUG && BuildConfig.ALLOW_LOCAL_HTTP && parsed.scheme.equals("http", true) &&
            (parsed.host?.equals("localhost", true) == true || parsed.host == "127.0.0.1" || parsed.host == "::1")
        if (!parsed.isAbsolute || parsed.host == null || parsed.userInfo != null ||
            (!parsed.scheme.equals("https", true) && !localOverride)) {
            throw IllegalStateException("La API debe usar HTTPS; HTTP solo se permite para localhost en debug explícito.")
        }
        return configured
    }
    private fun clearMemorySession() {
        if (coordinatorDelegate.isInitialized()) coordinator.invalidate { clearLocalStateWithoutCoordinator() }
        else clearLocalStateWithoutCoordinator()
    }
    private fun clearLocalStateWithoutCoordinator() {
        contexts.clear()
        accessToken = null; userId = null; displayName = null; expiresAt = 0L
    }
    private fun clearCredentials() {
        if (coordinatorDelegate.isInitialized()) coordinator.invalidate { clearLocalStateWithoutCoordinator(); sessionStore.clear() }
        else { clearLocalStateWithoutCoordinator(); sessionStore.clear() }
    }
    private fun beginAccountReplacement(): Long = coordinator.invalidate {
        clearLocalStateWithoutCoordinator(); sessionStore.clear(); coordinator.currentSessionGeneration()
    }
    private fun boundedExecutor(prefix: String) = ThreadPoolExecutor(2, 2, 0L, TimeUnit.MILLISECONDS, java.util.concurrent.ArrayBlockingQueue(32), { runnable ->
        Thread(runnable, "$prefix-${UUID.randomUUID()}").apply { isDaemon = true }
    }, ThreadPoolExecutor.AbortPolicy())
    private fun resolveVoid(call: PluginCall) = call.resolve(JSObject())
    private fun sessionObject(authenticated: Boolean, id: String?, name: String?) = JSObject().put("authenticated", authenticated).put("userId", id).put("displayName", name)
    private fun responseObject(response: NativeApiResponse) = JSObject().put("status", response.status).put("headers", JSONObject(response.headers as Map<*, *>)).put("body", response.body)
    private fun stringMap(json: JSONObject): Map<String, String> = json.keys().asSequence().associateWith { json.getString(it) }
    private fun JSONObject.toJSObject(): JSObject = JSObject(this.toString())
}
