package com.enigmabudget.dev

import java.util.concurrent.CompletableFuture
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class NativeLogoutResultTest {
    @Test fun RevocationIsUnconfirmedAfterTheThreeSecondDeadline() {
        val pendingNetworkResult = CompletableFuture<Boolean>()
        val started = System.nanoTime()
        assertFalse(awaitLogoutRevocation(pendingNetworkResult))
        val elapsedMillis = (System.nanoTime() - started) / 1_000_000
        assertTrue("must wait for the configured three-second deadline: $elapsedMillis ms", elapsedMillis >= NATIVE_LOGOUT_RESULT_DEADLINE_MILLIS - 100)
        pendingNetworkResult.complete(true)
    }

    @Test fun ImmediateConfirmedResultIsReportedOnlyWhenTrue() {
        assertTrue(awaitLogoutRevocation(CompletableFuture.completedFuture(true)))
        assertFalse(awaitLogoutRevocation(CompletableFuture.completedFuture(false)))
    }
}
