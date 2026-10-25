package com.enigmabudget.dev

import org.junit.Assert.assertEquals
import org.junit.Assert.assertThrows
import org.junit.Test

class AppLinksPolicyTest {
    @Test fun acceptsBareDnsHostAndOmitsFilterWhenAbsent() {
        assertEquals("invitaciones.example.test", AppLinksPolicy.validate("invitaciones.example.test"))
        assertEquals(null, AppLinksPolicy.validate(null))
        assertEquals(null, AppLinksPolicy.validate(""))
    }

    @Test fun rejectsValuesThatCouldBroadenOrBreakTheIntentFilter() {
        listOf(
            "*.example.test", "https://example.test", "example.test:443", "example.test/invitar",
            "example.test?x=1", "example.test#invite", " example.test", "EXAMPLE.TEST",
            "localhost", "-bad.example.test", "bad-.example.test", "a..example.test",
        ).forEach { host -> assertThrows(host, IllegalArgumentException::class.java) { AppLinksPolicy.validate(host) } }
    }
}
