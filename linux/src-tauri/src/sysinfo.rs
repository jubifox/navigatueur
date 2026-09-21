//! Port of `MemoryMonitorService` (C#).
//!
//! Windows sums the app's process plus every msedgewebview2.exe whose command
//! line references our own user-data folder, via WMI. There is no WMI here and
//! no need for one: WebKitGTK's helper processes (WebKitWebProcess,
//! WebKitNetworkProcess) are our direct children, so walking /proc for
//! processes whose parent chain leads back to us is both simpler and exact.

use std::collections::HashMap;

/// Resident set size of this process and all its descendants, in megabytes.
pub fn used_megabytes() -> u64 {
    let me = std::process::id();
    let mut parents: HashMap<u32, u32> = HashMap::new();
    let mut rss: HashMap<u32, u64> = HashMap::new();

    let Ok(entries) = std::fs::read_dir("/proc") else {
        return 0;
    };

    for entry in entries.flatten() {
        let Ok(name) = entry.file_name().into_string() else { continue };
        let Ok(pid) = name.parse::<u32>() else { continue };

        // /proc/<pid>/statm reports pages; field 2 is resident set size.
        let Ok(statm) = std::fs::read_to_string(format!("/proc/{pid}/statm")) else { continue };
        let Some(resident_pages) = statm.split_whitespace().nth(1).and_then(|v| v.parse::<u64>().ok())
        else {
            continue;
        };

        let Ok(stat) = std::fs::read_to_string(format!("/proc/{pid}/stat")) else { continue };
        let Some(ppid) = parse_ppid(&stat) else { continue };

        parents.insert(pid, ppid);
        rss.insert(pid, resident_pages * page_size());
    }

    let total: u64 = rss
        .iter()
        .filter(|(pid, _)| **pid == me || descends_from(**pid, me, &parents))
        .map(|(_, bytes)| *bytes)
        .sum();

    total / (1024 * 1024)
}

/// The parent pid is field 4 of /proc/<pid>/stat, but field 2 is the command
/// name in parentheses and may itself contain spaces or parentheses — so the
/// fields are counted from the *last* ')' rather than by splitting the line.
fn parse_ppid(stat: &str) -> Option<u32> {
    let after_comm = &stat[stat.rfind(')')? + 1..];
    after_comm.split_whitespace().nth(1)?.parse().ok()
}

fn descends_from(mut pid: u32, ancestor: u32, parents: &HashMap<u32, u32>) -> bool {
    // Bounded rather than `loop`: a /proc snapshot taken while processes come
    // and go can contain a cycle, and this must not hang the timer thread.
    for _ in 0..64 {
        match parents.get(&pid) {
            Some(&parent) if parent == ancestor => return true,
            Some(&parent) if parent <= 1 => return false,
            Some(&parent) => pid = parent,
            None => return false,
        }
    }
    false
}

fn page_size() -> u64 {
    // Every architecture this ships on uses 4 KiB pages; sysconf would need libc.
    4096
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn ppid_survives_a_command_name_containing_spaces_and_parens() {
        let stat = "1234 (weird )name( here) S 42 1234 1234 0 -1 4194304 100";
        assert_eq!(parse_ppid(stat), Some(42));
    }

    #[test]
    fn ppid_handles_a_plain_name() {
        assert_eq!(parse_ppid("7 (bash) S 3 7 7 0 -1 0 0"), Some(3));
    }

    #[test]
    fn descendancy_walks_the_chain() {
        let parents = HashMap::from([(5u32, 4u32), (4, 3), (3, 1)]);
        assert!(descends_from(5, 3, &parents));
        assert!(!descends_from(5, 9, &parents));
    }

    #[test]
    fn descendancy_terminates_on_a_cycle() {
        let parents = HashMap::from([(5u32, 6u32), (6, 5)]);
        assert!(!descends_from(5, 99, &parents));
    }

    #[test]
    fn reports_a_plausible_footprint_for_this_test_process() {
        assert!(used_megabytes() > 0);
    }
}
