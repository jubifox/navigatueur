"use strict";
// The Windows app's phishing list (~400 000 domains): a host matches when it
// or one of its parent domains is listed. Kept as a Set of strings — fine
// for a desktop's memory, and simple.

class DomainSet {
  constructor(set) {
    this.set = set || new Set();
  }

  get size() {
    return this.set.size;
  }

  has(host) {
    if (!host) return false;
    let candidate = String(host).toLowerCase().replace(/\.$/, "");
    for (;;) {
      if (this.set.has(candidate)) return true;
      const dot = candidate.indexOf(".");
      if (dot < 0) return false;
      candidate = candidate.slice(dot + 1);
      // Never match a bare top-level domain ("com") however the list was written.
      if (!candidate.includes(".")) return false;
    }
  }

  /** One domain per line; "#" comments, blank lines and hosts-file prefixes are skipped. */
  static parse(lines) {
    const set = new Set();
    for (const raw of lines) {
      let line = String(raw).trim();
      if (!line || line.startsWith("#")) continue;
      line = line.split(/\s+/).pop();
      line = line.replace(/^\*\./, "").replace(/^www\./, "").toLowerCase().replace(/\.$/, "");
      if (line.includes(".")) set.add(line);
    }
    return new DomainSet(set);
  }
}

module.exports = { DomainSet };
