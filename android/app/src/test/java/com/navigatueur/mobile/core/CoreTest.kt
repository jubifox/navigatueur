package com.navigatueur.mobile.core

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

/** Same matrix as the Windows app's UrlHelperTests / NavigationTests, so both address bars behave alike. */
class UrlHelperTest {
    @Test fun alreadyHasScheme_passesThrough() {
        assertEquals("https://example.com", UrlHelper.normalize("https://example.com"))
        assertEquals("http://example.com/path", UrlHelper.normalize("http://example.com/path"))
        assertEquals("about:config", UrlHelper.normalize("about:config"))
    }

    @Test fun looksLikeHost_prependsHttps() {
        assertEquals("https://example.com", UrlHelper.normalize("example.com"))
        assertEquals("https://example.com/path", UrlHelper.normalize("example.com/path"))
        assertEquals("https://localhost:5000", UrlHelper.normalize("localhost:5000"))
        assertEquals("https://example.com:8443/x", UrlHelper.normalize("example.com:8443/x"))
    }

    @Test fun plainText_becomesSearchQuery() {
        val result = UrlHelper.normalize("meilleur navigateur web")
        assertEquals("https://www.bing.com/search?q=meilleur%20navigateur%20web", result)
        assertEquals("https://duckduckgo.com/?q=chat", UrlHelper.normalize("chat", SearchEngines.byId("DuckDuckGo").queryPrefix))
        assertTrue(UrlHelper.normalize("c'est quoi ?").startsWith("https://www.bing.com/search?q="))
    }

    @Test fun emptyInput_returnsEmpty() = assertEquals("", UrlHelper.normalize("   "))

    @Test fun displayHost_dropsWww() {
        assertEquals("example.com", UrlHelper.displayHost("https://www.example.com/a"))
        assertEquals("fr.wikipedia.org", UrlHelper.displayHost("https://fr.wikipedia.org/"))
        assertNull(UrlHelper.displayHost("pas une adresse"))
        assertNull(UrlHelper.displayHost("about:blank"))
    }

    @Test fun normalizeKey_ignoresCaseDefaultPortSlashAndFragment() {
        assertEquals("https://example.com", UrlHelper.normalizeKey("https://Example.com/"))
        assertEquals("https://example.com/page", UrlHelper.normalizeKey("https://example.com/page#section"))
        assertEquals("https://example.com/a", UrlHelper.normalizeKey("HTTPS://EXAMPLE.COM:443/a"))
        assertTrue(UrlHelper.normalizeKey("https://example.com/Page") != UrlHelper.normalizeKey("https://example.com/page"))
    }

    @Test fun searchTermsOf_readsTheQueryBack() {
        assertEquals("meilleur navigateur", SearchEngines.searchTermsOf("https://www.bing.com/search?q=meilleur%20navigateur&form=x"))
        assertNull(SearchEngines.searchTermsOf("https://example.com/?q=x"))
    }
}

class AddressSuggestionsTest {
    private val sources = sequenceOf(
        SuggestionSource("https://www.youtube.com/", "YouTube", SuggestionKind.History),
        SuggestionSource("https://fr.wikipedia.org/wiki/Navigateur_web", "Navigateur web — Wikipédia", SuggestionKind.History),
        SuggestionSource("https://github.com/jubifox/navigatueur", "Navigatueur sur GitHub", SuggestionKind.Bookmark),
        SuggestionSource("https://www.youtube.com", "YouTube (doublon)", SuggestionKind.Bookmark),
    )

    @Test fun hostPrefixWins_andEachPageAppearsOnce() {
        val result = AddressSuggestions.rank("you", sources)
        assertEquals(1, result.size)
        assertEquals(SuggestionKind.Bookmark, result[0].kind)
    }

    @Test fun titleWordsMatch() {
        val result = AddressSuggestions.rank("navig", sources)
        assertEquals("https://github.com/jubifox/navigatueur", result[0].url)
        assertEquals(2, result.size)
    }

    @Test fun emptyQuery_suggestsNothing() = assertTrue(AddressSuggestions.rank("  ", sources).isEmpty())
}

class DomainSetTest {
    private val set = DomainSet.parse(sequenceOf("# liste", "evil.example", "www.phish.test", "0.0.0.0 hosts.example", "", "*.wild.test", "com"))

    @Test fun matchesTheDomainAndItsSubdomains() {
        assertTrue(set.contains("evil.example"))
        assertTrue(set.contains("login.evil.example"))
        assertTrue(set.contains("PHISH.TEST"))
        assertTrue(set.contains("www.phish.test"))
        assertTrue(set.contains("hosts.example"))
        assertTrue(set.contains("a.wild.test"))
    }

    @Test fun leavesOtherDomainsAlone() {
        assertFalse(set.contains("example"))
        assertFalse(set.contains("notevil.example.org"))
        assertFalse(set.contains("google.com"))
        assertFalse(set.contains(null))
        assertEquals(4, set.size)
    }

    @Test fun survivesARoundTripThroughItsHashes() =
        assertTrue(DomainSet.fromHashes(set.toLongArray()).contains("evil.example"))
}

class VersionsTest {
    @Test fun comparesNumerically() {
        assertTrue(Versions.isNewer("0.22.0", "0.21.0"))
        assertTrue(Versions.isNewer("v0.10.0", "0.9.9"))
        assertFalse(Versions.isNewer("0.21.0", "0.21.0"))
        assertFalse(Versions.isNewer("0.21.0-rc1", "0.21.0"))
        assertFalse(Versions.isNewer("pas une version", "0.21.0"))
    }

    @Test fun picksTheApkForTheDevice() {
        val assets = listOf(
            "NavigatueurSetup-0.22.0.exe",
            "Navigatueur-Android-0.22.0-armeabi-v7a.apk",
            "Navigatueur-Android-0.22.0-arm64-v8a.apk",
            "Navigatueur-Android-0.22.0-x86_64.apk",
        )
        assertEquals("Navigatueur-Android-0.22.0-arm64-v8a.apk", Versions.pickApk(assets, listOf("arm64-v8a", "armeabi-v7a")))
        assertEquals("Navigatueur-Android-0.22.0-armeabi-v7a.apk", Versions.pickApk(assets, listOf("armeabi-v7a")))
        assertNull(Versions.pickApk(assets, listOf("riscv64")))
    }
}
