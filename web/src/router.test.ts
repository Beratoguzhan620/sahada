import { describe, expect, it } from "vitest";
import { parseRoute } from "./router";

const c = "0195f0aa-1111-7222-8333-444455556666";
const o = "0195f0aa-9999-7222-8333-444455556666";

describe("parseRoute", () => {
  it("reads company pages", () => {
    expect(parseRoute(`/c/${c}`)).toEqual({ page: "board", companyId: c, date: undefined });
    expect(parseRoute(`/c/${c}/board`, "?date=2026-11-05")).toEqual({ page: "board", companyId: c, date: "2026-11-05" });
    expect(parseRoute(`/c/${c}/board`, "?date=tomorrow")).toEqual({ page: "board", companyId: c, date: undefined });
    expect(parseRoute(`/c/${c}/orders/${o}`)).toEqual({ page: "order", companyId: c, orderId: o });
    expect(parseRoute(`/c/${c}/orders/new`, `?customer=${o}`)).toEqual({ page: "order-new", companyId: c, customerId: o });
  });

  it("reads the technician app", () => {
    expect(parseRoute("/tech")).toEqual({ page: "tech" });
    expect(parseRoute(`/tech/${o}`)).toEqual({ page: "tech", orderId: o });
  });

  it("only follows addresses within the app after signing in", () => {
    expect(parseRoute("/signin", "?next=/tech")).toEqual({ page: "signin", next: "/tech" });
    expect(parseRoute("/signin", "?next=//evil.example")).toEqual({ page: "signin", next: undefined });
  });

  it("does not guess at unknown addresses", () => {
    expect(parseRoute("/c/not-an-id/board").page).toBe("not-found");
    expect(parseRoute(`/c/${c}/orders/123`).page).toBe("not-found");
  });
});
