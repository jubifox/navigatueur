package com.navigatueur.mobile.core

/** "0.22.0", "v0.22.0", "0.22.0-rc1" → comparable; anything after a "-" is ignored, like the Windows updater. */
object Versions {
    fun parse(text: String?): List<Int>? {
        val core = text?.trim()?.removePrefix("v")?.removePrefix("V")?.substringBefore('-')?.takeIf { it.isNotEmpty() } ?: return null
        val parts = core.split('.').map { it.toIntOrNull() ?: return null }
        return parts.takeIf { it.isNotEmpty() }
    }

    fun isNewer(candidate: String?, current: String?): Boolean {
        val a = parse(candidate) ?: return false
        val b = parse(current) ?: return true
        for (i in 0 until maxOf(a.size, b.size)) {
            val x = a.getOrElse(i) { 0 }
            val y = b.getOrElse(i) { 0 }
            if (x != y) return x > y
        }
        return false
    }

    /**
     * The APK of a release that fits this device, from the release's file
     * names ("Navigatueur-Android-0.22.0-arm64-v8a.apk"): the first of the
     * device's supported processor types that has one.
     */
    fun pickApk(assetNames: List<String>, supportedAbis: List<String>): String? {
        val apks = assetNames.filter { it.startsWith("Navigatueur-Android-") && it.endsWith(".apk") && !it.endsWith("-debug.apk") }
        for (abi in supportedAbis) {
            apks.firstOrNull { it.endsWith("-$abi.apk") }?.let { return it }
        }
        return null
    }
}
