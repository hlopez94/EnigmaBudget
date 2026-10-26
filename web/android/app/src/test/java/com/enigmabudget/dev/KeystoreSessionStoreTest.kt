package com.enigmabudget.dev

import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotEquals
import org.junit.Assert.assertNull
import org.junit.Test
import javax.crypto.SecretKey
import javax.crypto.spec.SecretKeySpec

class KeystoreSessionStoreTest {
    @Test fun Keystore_store_encrypts_reads_and_clears() {
        val keyProvider = FakeKeyProvider()
        val values = FakeValueStore()
        val store = KeystoreSessionStore(keyProvider, values)
        val token = "opaque-refresh-value"
        store.store(token)
        val ciphertext = values.value!!
        assertNotEquals(token, ciphertext.toString(Charsets.UTF_8))
        assertEquals(token, store.read())
        store.clear()
        assertNull(values.value)
        assertNull(store.read())
    }

    @Test fun Key_loss_makes_existing_ciphertext_unreadable() {
        val provider = FakeKeyProvider()
        val values = FakeValueStore()
        val store = KeystoreSessionStore(provider, values)
        store.store("opaque-refresh-value")
        val ciphertext = values.value!!.copyOf()
        provider.delete()
        assertNull(store.read())
        assertArrayEquals(ciphertext, values.value)
    }

    private class FakeValueStore : EncryptedValueStore {
        var value: ByteArray? = null
        override fun read() = value?.copyOf()
        override fun write(value: ByteArray) { this.value = value.copyOf() }
        override fun clear() { value = null }
    }

    private class FakeKeyProvider : SessionKeyProvider {
        private var key = newKey()
        override fun getOrCreate(): SecretKey = key
        override fun delete() { key = newKey() }
        private fun newKey() = SecretKeySpec(ByteArray(32) { (it + (System.nanoTime() % 127)).toByte() }, "AES")
    }
}
