package com.enigmabudget.dev

/** Accepts only a bare, lowercase public DNS hostname for the /invitar App Link. */
object AppLinksPolicy {
    private val hostname = Regex("^(?=.{1,253}$)(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\\.)+[a-z]{2,63}$")

    fun validate(host: String?): String? {
        if (host.isNullOrEmpty()) return null
        require(hostname.matches(host)) {
            "El host de App Links debe ser un dominio DNS en minúsculas, sin esquema, puerto, ruta ni espacios."
        }
        return host
    }
}
