package com.enigmabudget.dev

import android.content.Context
import android.content.SharedPreferences
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import java.security.KeyStore
import javax.crypto.Cipher
import javax.crypto.KeyGenerator
import javax.crypto.SecretKey
import javax.crypto.spec.GCMParameterSpec

interface SessionKeyProvider {
    fun getOrCreate(): SecretKey
    fun delete()
}

interface EncryptedValueStore {
    fun read(): ByteArray?
    fun write(value: ByteArray)
    fun clear()
}

/** Stores only an AES-GCM ciphertext and IV. The refresh credential never enters WebView storage. */
class KeystoreSessionStore(
    private val keyProvider: SessionKeyProvider,
    private val values: EncryptedValueStore,
) {
    @Synchronized
    fun store(refreshToken: String) {
        require(refreshToken.isNotBlank()) { "A refresh credential is required." }
        val cipher = Cipher.getInstance("AES/GCM/NoPadding")
        cipher.init(Cipher.ENCRYPT_MODE, keyProvider.getOrCreate())
        val encrypted = cipher.doFinal(refreshToken.toByteArray(Charsets.UTF_8))
        val iv = cipher.iv
        values.write(byteArrayOf(iv.size.toByte()) + iv + encrypted)
    }

    @Synchronized
    fun read(): String? {
        val payload = values.read() ?: return null
        try {
            val ivSize = payload.firstOrNull()?.toInt()?.and(0xff) ?: return null
            if (ivSize != 12 || payload.size <= ivSize + 1) return null
            val iv = payload.copyOfRange(1, 1 + ivSize)
            val ciphertext = payload.copyOfRange(1 + ivSize, payload.size)
            val cipher = Cipher.getInstance("AES/GCM/NoPadding")
            cipher.init(Cipher.DECRYPT_MODE, keyProvider.getOrCreate(), GCMParameterSpec(128, iv))
            return cipher.doFinal(ciphertext).toString(Charsets.UTF_8)
        } catch (_: Exception) {
            return null
        }
    }

    @Synchronized
    fun clear() {
        values.clear()
        keyProvider.delete()
    }
}

class SharedPreferencesEncryptedValueStore(context: Context) : EncryptedValueStore {
    private val preferences: SharedPreferences = context.getSharedPreferences("native_session_ciphertext", Context.MODE_PRIVATE)
    override fun read(): ByteArray? = preferences.getString(KEY, null)?.let { android.util.Base64.decode(it, android.util.Base64.NO_WRAP) }
    override fun write(value: ByteArray) {
        check(preferences.edit().putString(KEY, android.util.Base64.encodeToString(value, android.util.Base64.NO_WRAP)).commit()) {
            "Encrypted session data could not be persisted."
        }
    }
    override fun clear() {
        check(preferences.edit().remove(KEY).commit()) { "Encrypted session data could not be cleared." }
    }
    private companion object { const val KEY = "encrypted_refresh" }
}

class AndroidKeystoreSessionKeyProvider : SessionKeyProvider {
    override fun getOrCreate(): SecretKey {
        val store = KeyStore.getInstance("AndroidKeyStore").apply { load(null) }
        (store.getKey(ALIAS, null) as? SecretKey)?.let { return it }
        val generator = KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, "AndroidKeyStore")
        generator.init(KeyGenParameterSpec.Builder(ALIAS, KeyProperties.PURPOSE_ENCRYPT or KeyProperties.PURPOSE_DECRYPT)
            .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
            .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
            .setRandomizedEncryptionRequired(true)
            .build())
        return generator.generateKey()
    }

    override fun delete() {
        val store = KeyStore.getInstance("AndroidKeyStore").apply { load(null) }
        if (store.containsAlias(ALIAS)) store.deleteEntry(ALIAS)
    }

    private companion object { const val ALIAS = "com.enigmabudget.native.session.v1" }
}
