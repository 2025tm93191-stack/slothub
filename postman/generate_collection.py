"""Generates postman/SlotHub.postman_collection.json and postman/SlotHub.local.postman_environment.json.

    python3 postman/generate_collection.py postman
"""
import json
import sys
import uuid

out_dir = sys.argv[1]


def req(name, method, url, body=None, key=None, tests=(), prerequest=(), description=""):
    headers = [{"key": "Content-Type", "value": "application/json"}] if body is not None else []
    if key:
        headers.append({"key": "Idempotency-Key", "value": key})
    item = {
        "name": name,
        "request": {
            "method": method,
            "header": headers,
            "url": url,
            "description": description,
        },
        "event": [],
    }
    if body is not None:
        item["request"]["body"] = {"mode": "raw", "raw": json.dumps(body, indent=2), "options": {"raw": {"language": "json"}}}
    if prerequest:
        item["event"].append({"listen": "prerequest", "script": {"type": "text/javascript", "exec": list(prerequest)}})
    if tests:
        item["event"].append({"listen": "test", "script": {"type": "text/javascript", "exec": list(tests)}})
    return item


def status(code):
    return f'pm.test("status {code}", () => pm.response.to.have.status({code}));'


def js_path(path):
    return f"pm.response.json(){path if path.startswith('[') else '.' + path}"


def field(path, value):
    return f'pm.test("{path} is {value}", () => pm.expect({js_path(path)}).to.eql({json.dumps(value)}));'


def save(var, path):
    return f'pm.collectionVariables.set("{var}", {js_path(path)});'


def new_key(var):
    return f'pm.collectionVariables.set("{var}", pm.variables.replaceIn("{{{{$guid}}}}"));'


S, B, P, N = "{{schedulingUrl}}", "{{bookingUrl}}", "{{paymentUrl}}", "{{notificationUrl}}"
booking_body = {"tenantId": "{{tenantId}}", "slotId": "{{slotId}}", "customer": {"id": "postman-customer", "phone": "+919800000001", "email": "priya@example.com"}}

health = {
    "name": "0. Health",
    "item": [req(f"{name} ready", "GET", f"{url}/health/ready", tests=[status(200), field("status", "HEALTHY")])
             for name, url in [("Scheduling", S), ("Booking", B), ("Payment", P), ("Notification", N)]],
}

setup = {
    "name": "1. Setup catalog and slots (Scheduling)",
    "item": [
        req("Create tenant", "POST", f"{S}/api/v1/tenants", {"name": "Sunrise Dental {{$randomInt}}", "timeZone": "Asia/Kolkata"},
            tests=[status(201), save("tenantId", "id")]),
        req("Create resource", "POST", f"{S}/api/v1/tenants/{{{{tenantId}}}}/resources", {"name": "Dr. Mehta", "type": "person"},
            tests=[status(201), save("resourceId", "id")]),
        req("Create offering", "POST", f"{S}/api/v1/tenants/{{{{tenantId}}}}/offerings",
            {"name": "Teeth cleaning", "durationMinutes": 30, "priceMinor": 50000, "currency": "INR"},
            tests=[status(201), save("offeringId", "id")]),
        req("Create slots (capacity 1)", "POST", f"{S}/api/v1/slots",
            {"resourceId": "{{resourceId}}", "offeringId": "{{offeringId}}",
             "slots": [{"startAt": "{{slotStart1}}", "capacity": 1}, {"startAt": "{{slotStart2}}", "capacity": 1}, {"startAt": "{{slotStart3}}", "capacity": 1}]},
            prerequest=[
                "// three back-to-back slots starting tomorrow on the hour",
                "const start = new Date(Date.now() + 24 * 3600 * 1000); start.setUTCMinutes(0, 0, 0);",
                "[1, 2, 3].forEach(i => pm.collectionVariables.set('slotStart' + i, new Date(start.getTime() + (i - 1) * 1800 * 1000).toISOString()));",
            ],
            tests=[status(201), save("slotId", "[0].id"), save("slotId2", "[1].id"), save("slotId3", "[2].id"),
                   'pm.test("3 slots created", () => pm.expect(pm.response.json()).to.have.length(3));']),
        req("Search available slots", "GET", f"{S}/api/v1/slots?tenantId={{{{tenantId}}}}&available=true",
            tests=[status(200), 'pm.test("3 available", () => pm.expect(pm.response.json().total).to.eql(3));']),
        req("Overlapping slot is rejected (409 SLOT_OVERLAP)", "POST", f"{S}/api/v1/slots",
            {"resourceId": "{{resourceId}}", "offeringId": "{{offeringId}}", "slots": [{"startAt": "{{slotStart1}}", "capacity": 1}]},
            tests=[status(409), field("code", "SLOT_OVERLAP")]),
        req("Validation error (400 with field errors)", "POST", f"{S}/api/v1/tenants", {"name": "", "timeZone": "Mars/Base"},
            tests=[status(400), field("code", "VALIDATION_FAILED"),
                   'pm.test("field errors listed", () => pm.expect(pm.response.json().errors).to.have.keys("Name", "TimeZone"));']),
    ],
}

happy = {
    "name": "2. Main flow: hold → pay → confirm → notify → cancel",
    "item": [
        req("Create booking (hold slot)", "POST", f"{B}/api/v1/bookings", booking_body, key="{{createKey}}",
            prerequest=[new_key("createKey")],
            tests=[status(201), field("status", "HELD"), save("bookingId", "id"), save("holdId", "holdId")],
            description="Step 1. Requires Idempotency-Key. The slot is held until holdExpiresAt (HOLD_TTL_SECONDS)."),
        req("Repeat create with SAME key → replayed (BR4)", "POST", f"{B}/api/v1/bookings", booking_body, key="{{createKey}}",
            tests=[status(201),
                   'pm.test("replayed header", () => pm.expect(pm.response.headers.get("Idempotency-Replayed")).to.eql("true"));',
                   'pm.test("same booking id", () => pm.expect(pm.response.json().id).to.eql(pm.collectionVariables.get("bookingId")));']),
        req("Same key, different body → 422 (BR4)", "POST", f"{B}/api/v1/bookings",
            {**booking_body, "customer": {"id": "someone-else", "phone": "+919800000002"}}, key="{{createKey}}",
            tests=[status(422), field("code", "IDEMPOTENCY_KEY_MISMATCH")]),
        req("Missing Idempotency-Key → 400", "POST", f"{B}/api/v1/bookings", booking_body,
            tests=[status(400), field("code", "IDEMPOTENCY_KEY_REQUIRED")]),
        req("Slot is now full for another customer → 409 (BR1)", "POST", f"{B}/api/v1/bookings",
            {**booking_body, "customer": {"id": "late-customer", "phone": "+919800000003"}}, key="{{$guid}}",
            tests=[status(409), field("code", "SLOT_UNAVAILABLE")]),
        req("Pay and confirm (BR3)", "POST", f"{B}/api/v1/bookings/{{{{bookingId}}}}/payment", key="{{$guid}}",
            tests=[status(200), field("status", "CONFIRMED"), save("paymentId", "paymentId")]),
        req("Get booking with history", "GET", f"{B}/api/v1/bookings/{{{{bookingId}}}}",
            tests=[status(200), field("status", "CONFIRMED"),
                   'pm.test("history PENDING→HELD→CONFIRMED", () => pm.expect(pm.response.json().history.map(h => h.to)).to.eql(["PENDING", "HELD", "CONFIRMED"]));']),
        req("Hold is CONFIRMED in Scheduling", "GET", f"{S}/api/v1/holds/{{{{holdId}}}}", tests=[status(200), field("status", "CONFIRMED")]),
        req("One successful payment in Payment", "GET", f"{P}/api/v1/payments?bookingId={{{{bookingId}}}}",
            tests=[status(200), 'pm.test("exactly one payment", () => pm.expect(pm.response.json()).to.have.length(1));']),
        req("Notifications for the booking", "GET", f"{N}/api/v1/notifications?bookingId={{{{bookingId}}}}",
            prerequest=["// the outbox delivers within a few seconds", "setTimeout(() => {}, 4000);"],
            tests=[status(200), 'pm.test("confirmation queued", () => pm.expect(pm.response.json().items.map(i => i.eventType)).to.include("BOOKING_CONFIRMED"));']),
        req("Cancel → refund per BR5", "POST", f"{B}/api/v1/bookings/{{{{bookingId}}}}/cancel", key="{{$guid}}",
            tests=[status(200), field("status", "CANCELLED"),
                   'pm.test("refund > 0", () => pm.expect(pm.response.json().refundedMinor).to.be.above(0));']),
        req("Slot capacity returned after cancel", "GET", f"{S}/api/v1/slots/{{{{slotId}}}}", tests=[status(200), field("reserved", 0)]),
    ],
}

failures = {
    "name": "3. Failure cases",
    "item": [
        req("Create booking on slot 2", "POST", f"{B}/api/v1/bookings", {**booking_body, "slotId": "{{slotId2}}"}, key="{{$guid}}",
            tests=[status(201), save("bookingId2", "id")]),
        req("Payment gateway: decline everything", "PUT", f"{P}/api/v1/admin/chaos", {"failureRate": 1, "latencyMs": 0, "mode": "DECLINE"},
            tests=[status(204)]),
        req("Pay → 402 PAYMENT_DECLINED, slot released", "POST", f"{B}/api/v1/bookings/{{{{bookingId2}}}}/payment", key="{{$guid}}",
            tests=[status(402), field("code", "PAYMENT_DECLINED")]),
        req("Payment gateway: back to normal", "PUT", f"{P}/api/v1/admin/chaos", {"failureRate": 0, "latencyMs": 0, "mode": "DECLINE"},
            tests=[status(204)]),
        req("Booking is FAILED", "GET", f"{B}/api/v1/bookings/{{{{bookingId2}}}}", tests=[status(200), field("status", "FAILED")]),
        req("Slot 2 capacity returned", "GET", f"{S}/api/v1/slots/{{{{slotId2}}}}", tests=[status(200), field("reserved", 0)]),
        req("Unknown booking → 404", "GET", f"{B}/api/v1/bookings/00000000-0000-0000-0000-000000000001",
            tests=[status(404), field("code", "BOOKING_NOT_FOUND")]),
        req("Cancel an already failed booking → 409", "POST", f"{B}/api/v1/bookings/{{{{bookingId2}}}}/cancel", key="{{$guid}}",
            tests=[status(409), field("code", "INVALID_STATE_TRANSITION")]),
    ],
}

direct = {
    "name": "4. Payment and Notification directly",
    "item": [
        req("Charge", "POST", f"{P}/api/v1/payments", {"bookingId": "{{$guid}}", "amountMinor": 50000, "currency": "INR"}, key="{{$guid}}",
            tests=[status(201), field("status", "SUCCEEDED"), save("directPaymentId", "id")]),
        req("Partial refund", "POST", f"{P}/api/v1/payments/{{{{directPaymentId}}}}/refunds", {"amountMinor": 20000}, key="{{$guid}}",
            tests=[status(201), field("payment.status", "PARTIALLY_REFUNDED")]),
        req("Refund more than remaining → 409", "POST", f"{P}/api/v1/payments/{{{{directPaymentId}}}}/refunds", {"amountMinor": 40000}, key="{{$guid}}",
            tests=[status(409), field("code", "REFUND_EXCEEDS_PAYMENT")]),
        req("Send notification", "POST", f"{N}/api/v1/notifications",
            {"bookingId": "{{$guid}}", "eventType": "PAYMENT_FAILED", "channel": "SMS", "recipient": "+919800000001"},
            tests=[status(202), field("status", "PENDING")]),
        req("Get chaos settings (Payment)", "GET", f"{P}/api/v1/admin/chaos", tests=[status(200)]),
    ],
}

admin = {
    "name": "5. Admin (Challenge 3B)",
    "item": [
        req("Run hold-expiry sweep now", "POST", f"{S}/api/v1/admin/holds/expire-now", tests=[status(200)],
            description="Expires HELD holds past their TTL. Running it twice is safe."),
        req("Run it again (safe: expires nothing new)", "POST", f"{S}/api/v1/admin/holds/expire-now", tests=[status(200)]),
    ],
}

collection = {
    "info": {
        "_postman_id": str(uuid.uuid5(uuid.NAMESPACE_URL, "slothub-collection")),
        "name": "SlotHub",
        "description": "SlotHub microservices — run top to bottom (Runner or newman). Each folder is self-contained after folder 1.\n"
                       "Start the stack first: docker compose up -d --build --wait",
        "schema": "https://schema.getpostman.com/json/collection/v2.1.0/collection.json",
    },
    "item": [health, setup, happy, failures, direct, admin],
    "event": [{"listen": "prerequest", "script": {"type": "text/javascript", "exec": [
        "pm.request.headers.upsert({ key: 'X-Correlation-Id', value: 'postman-' + pm.variables.replaceIn('{{$guid}}').slice(0, 8) });"]}}],
    "variable": [{"key": k, "value": ""} for k in [
        "tenantId", "resourceId", "offeringId", "slotId", "slotId2", "slotId3", "slotStart1", "slotStart2", "slotStart3",
        "createKey", "bookingId", "bookingId2", "holdId", "paymentId", "directPaymentId"]],
}

environment = {
    "id": str(uuid.uuid5(uuid.NAMESPACE_URL, "slothub-env-local")),
    "name": "SlotHub local",
    "values": [{"key": k, "value": v, "enabled": True} for k, v in [
        ("schedulingUrl", "http://localhost:5101"), ("bookingUrl", "http://localhost:5102"),
        ("paymentUrl", "http://localhost:5103"), ("notificationUrl", "http://localhost:5104")]],
    "_postman_variable_scope": "environment",
}

with open(f"{out_dir}/SlotHub.postman_collection.json", "w") as f:
    json.dump(collection, f, indent=2)
with open(f"{out_dir}/SlotHub.local.postman_environment.json", "w") as f:
    json.dump(environment, f, indent=2)
print("requests:", sum(len(folder["item"]) for folder in collection["item"]))
