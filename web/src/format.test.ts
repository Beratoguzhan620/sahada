import { describe, expect, it } from "vitest";
import { addDays, formatMoney, initials, localMinutes, minutes, relative, today, zonedInstant } from "./format";

describe("format", () => {
  it("writes money the Turkish way", () => {
    expect(formatMoney(1249.9).replace(/\s/g, " ")).toBe("₺1.249,90");
  });

  it("works on the company's wall clock", () => {
    expect(zonedInstant("2026-11-05", "09:30")).toBe("2026-11-05T06:30:00.000Z");
    expect(localMinutes("2026-11-05T06:30:00Z")).toBe(9 * 60 + 30);
    expect(today(new Date("2026-11-05T22:30:00Z"))).toBe("2026-11-06");
    expect(addDays("2026-12-31", 1)).toBe("2027-01-01");
  });

  it("says durations and distances in time plainly", () => {
    expect(minutes(45)).toBe("45 min");
    expect(minutes(90)).toBe("1 h 30 min");
    expect(minutes(120)).toBe("2 h");
    const now = Date.parse("2026-11-05T10:00:00Z");
    expect(relative("2026-11-05T13:00:00Z", now)).toBe("in 3 h");
    expect(relative("2026-11-03T10:00:00Z", now)).toBe("2 days ago");
    expect(relative("2026-11-05T09:58:00Z", now)).toBe("2 min ago");
  });

  it("makes initials from Turkish names", () => {
    expect(initials("ilkay şahin")).toBe("İŞ");
    expect(initials("Ali")).toBe("A");
  });
});
