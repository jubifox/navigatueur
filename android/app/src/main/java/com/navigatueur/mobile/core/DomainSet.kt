package com.navigatueur.mobile.core

/**
 * A large list of domains (the phishing list holds ~400 000) kept as sorted
 * 64-bit hashes: 8 bytes per domain instead of a String object each, so the
 * whole list fits in a few megabytes and a lookup is a binary search. A host
 * matches when it or one of its parent domains is listed — like the Windows
 * app's PhishingProtectionService. Two different domains sharing a 64-bit
 * hash is not a practical concern at this size.
 */
class DomainSet private constructor(private val hashes: LongArray) {
    val size: Int get() = hashes.size

    fun contains(host: String?): Boolean {
        if (host.isNullOrEmpty() || hashes.isEmpty()) return false
        var candidate = host.lowercase().trimEnd('.')
        while (true) {
            if (hashes.binarySearch(hash(candidate)) >= 0) return true
            val dot = candidate.indexOf('.')
            if (dot < 0) return false
            candidate = candidate.substring(dot + 1)
            // Never match a bare top-level domain ("com") however the list was written.
            if (!candidate.contains('.')) return false
        }
    }

    fun toLongArray(): LongArray = hashes.copyOf()

    companion object {
        val EMPTY = DomainSet(LongArray(0))

        /** One domain per line; blank lines and "#" comments are skipped, a leading "www." or "*." is dropped. */
        fun parse(lines: Sequence<String>): DomainSet {
            var buffer = LongArray(1 shl 16)
            var count = 0
            for (raw in lines) {
                var line = raw.trim()
                if (line.isEmpty() || line.startsWith("#")) continue
                // Hosts-file style ("0.0.0.0 example.com").
                line = line.substringAfterLast(' ').substringAfterLast('\t')
                line = line.removePrefix("*.").removePrefix("www.").lowercase().trimEnd('.')
                if (!line.contains('.')) continue
                if (count == buffer.size) buffer = buffer.copyOf(buffer.size * 2)
                buffer[count++] = hash(line)
            }
            return fromHashes(buffer.copyOf(count))
        }

        fun fromHashes(hashes: LongArray): DomainSet {
            val sorted = hashes.copyOf()
            sorted.sort()
            return DomainSet(sorted)
        }

        /** FNV-1a, 64 bits. */
        fun hash(text: String): Long {
            var h = -0x340d631b7bdddcdbL // 0xcbf29ce484222325
            for (ch in text) {
                h = h xor ch.code.toLong()
                h *= 0x100000001b3L
            }
            return h
        }
    }
}
