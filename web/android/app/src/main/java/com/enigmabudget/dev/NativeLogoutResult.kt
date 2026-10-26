package com.enigmabudget.dev

import java.util.concurrent.CompletableFuture
import java.util.concurrent.TimeUnit

internal const val NATIVE_LOGOUT_RESULT_DEADLINE_MILLIS = 3_000L

internal fun awaitLogoutRevocation(
    result: CompletableFuture<Boolean>?,
    timeoutMillis: Long = NATIVE_LOGOUT_RESULT_DEADLINE_MILLIS,
): Boolean = try {
    result?.get(timeoutMillis, TimeUnit.MILLISECONDS) == true
} catch (_: Exception) {
    false
}
