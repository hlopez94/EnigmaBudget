package com.enigmabudget.dev

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class NativeAuthOptionsProjectionTest {
    @Test fun ProjectsOnlyTheTwoBooleanOptionsAndDropsSecretShapedExtras() {
        val projected = NativeAuthOptionsProjection.project(NativeApiResponse(
            200,
            mapOf("content-type" to "application/json"),
            """{"googleConfigured":true,"developmentLoginEnabled":false,"accessToken":"secret-access","refreshToken":"secret-refresh","authorization":"Bearer secret-auth"}""",
        ))

        assertEquals(NativeAuthOptions(googleConfigured = true, developmentLoginEnabled = false), projected)
        assertFalse(projected.toString().contains("secret"))
    }

    @Test fun RejectsNonSuccessStatusWithoutEchoingResponseSecrets() {
        val error = org.junit.Assert.assertThrows(NativeHttpFailure::class.java) {
            NativeAuthOptionsProjection.project(NativeApiResponse(
                502, mapOf("content-type" to "application/json"),
                """{"accessToken":"secret-access","refreshToken":"secret-refresh"}""",
            ))
        }
        assertFalse(error.message.orEmpty().contains("secret"))
    }

    @Test fun RejectsMalformedJsonAndMissingOrNonBooleanOptionsWithoutEchoingInput() {
        val malformed = listOf(
            "not-json-secret",
            """{"googleConfigured":"true","developmentLoginEnabled":false,"accessToken":"secret"}""",
            """{"googleConfigured":true,"developmentLoginEnabled":null,"refreshToken":"secret"}""",
            """{"googleConfigured":true,"accessToken":"secret"}""",
        )
        malformed.forEach { body ->
            val error = org.junit.Assert.assertThrows(IllegalArgumentException::class.java) {
                NativeAuthOptionsProjection.project(NativeApiResponse(200, mapOf("content-type" to "application/json"), body))
            }
            assertFalse(error.message.orEmpty().contains("secret"))
        }
    }

    @Test fun RequiresJsonContentType() {
        val error = org.junit.Assert.assertThrows(IllegalArgumentException::class.java) {
            NativeAuthOptionsProjection.project(NativeApiResponse(200, mapOf("content-type" to "text/plain"), "{}"))
        }
        assertTrue(error.message.orEmpty().isNotBlank())
    }
}
