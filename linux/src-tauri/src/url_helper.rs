//! Port of `Navigatueur.Core.UrlHelper` (C#). Behaviour is kept identical so the
//! address bar parses the same text the same way on both platforms; the only
//! difference is that the search prefix is supplied by the caller instead of
//! being hardcoded to Bing, because the Linux build reads the engine from settings.

/// Turns free-form address-bar text into a navigable URL: passes through text
/// that already looks like a URL, and turns everything else into a search query.
pub fn normalize(input: &str, search_url: &str) -> String {
    let trimmed = input.trim();
    if trimmed.is_empty() {
        return String::new();
    }

    // Mirrors the C# `Uri.TryCreate(..., UriKind.Absolute)` + scheme allow-list.
    if let Ok(parsed) = url::Url::parse(trimmed) {
        if matches!(parsed.scheme(), "http" | "https" | "file" | "about") {
            return trimmed.to_string();
        }
    }

    if looks_like_host(trimmed) {
        return format!("https://{trimmed}");
    }

    format!("{search_url}?q={}", urlencoding_escape(trimmed))
}

fn looks_like_host(text: &str) -> bool {
    if text.contains(' ') {
        return false;
    }

    if text.eq_ignore_ascii_case("localhost") || text.to_ascii_lowercase().starts_with("localhost:")
    {
        return true;
    }

    let host_part = text.split('/').next().unwrap_or("");
    let host_part = host_part.split(':').next().unwrap_or("");
    host_part.contains('.') && !host_part.ends_with('.')
}

/// Equivalent of `Uri.EscapeDataString` — percent-encodes everything outside
/// RFC 3986's unreserved set.
fn urlencoding_escape(value: &str) -> String {
    let mut out = String::with_capacity(value.len());
    for byte in value.as_bytes() {
        match byte {
            b'A'..=b'Z' | b'a'..=b'z' | b'0'..=b'9' | b'-' | b'_' | b'.' | b'~' => {
                out.push(*byte as char)
            }
            _ => out.push_str(&format!("%{byte:02X}")),
        }
    }
    out
}

#[cfg(test)]
mod tests {
    use super::*;

    const SEARCH: &str = "https://www.bing.com/search";

    #[test]
    fn passes_through_absolute_urls() {
        assert_eq!(normalize("https://example.com", SEARCH), "https://example.com");
        assert_eq!(normalize("about:blank", SEARCH), "about:blank");
    }

    #[test]
    fn adds_scheme_to_bare_hosts() {
        assert_eq!(normalize("example.com", SEARCH), "https://example.com");
        assert_eq!(normalize("localhost:8080", SEARCH), "https://localhost:8080");
    }

    #[test]
    fn searches_everything_else() {
        assert_eq!(
            normalize("chat gpt", SEARCH),
            "https://www.bing.com/search?q=chat%20gpt"
        );
    }

    #[test]
    fn empty_input_stays_empty() {
        assert_eq!(normalize("   ", SEARCH), "");
    }

    /// The exact cases asserted by the C# `UrlHelperTests`, kept verbatim so a
    /// divergence between `Uri.TryCreate` and `url::Url::parse` shows up here.
    /// It matters: RFC 3986 permits dots in a scheme, so "example.com/path"
    /// *does* parse as a URL in Rust — it must still be treated as a bare host.
    #[test]
    fn matches_the_csharp_test_matrix() {
        assert_eq!(normalize("https://example.com", SEARCH), "https://example.com");
        assert_eq!(
            normalize("http://example.com/path", SEARCH),
            "http://example.com/path"
        );
        assert_eq!(normalize("example.com", SEARCH), "https://example.com");
        assert_eq!(
            normalize("example.com/path", SEARCH),
            "https://example.com/path"
        );
        assert_eq!(normalize("localhost:5000", SEARCH), "https://localhost:5000");

        let search = normalize("meilleur navigateur web", SEARCH);
        assert!(search.starts_with("https://www.bing.com/search?q="));
        assert!(search.contains("meilleur"));

        assert_eq!(normalize("   ", SEARCH), "");
    }

    #[test]
    fn trailing_dot_host_is_a_search() {
        assert!(normalize("example.", SEARCH).starts_with(SEARCH));
    }
}
