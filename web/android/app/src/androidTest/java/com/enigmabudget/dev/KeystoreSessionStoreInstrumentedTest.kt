package com.enigmabudget.dev

import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class KeystoreSessionStoreInstrumentedTest {
    @Test fun Android_Keystore_AES_GCM_round_trips_and_clears() {
        val context = InstrumentationRegistry.getInstrumentation().targetContext
        val values = SharedPreferencesEncryptedValueStore(context)
        val store = KeystoreSessionStore(AndroidKeystoreSessionKeyProvider(), values)
        store.clear()
        store.store("device-only-refresh-value")
        assertEquals("device-only-refresh-value", store.read())
        store.clear()
        assertNull(store.read())
    }
}
