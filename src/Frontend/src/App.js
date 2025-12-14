import React, { useEffect, useState } from "react";
import * as signalR from "@microsoft/signalr"; 
import { getEventBridgeUrl } from "./env";


export default function App() {
  const [connection, setConnection] = useState(null);
  const [status, setStatus] = useState("Disconnected");
  const [userEvents, setUserEvents] = useState([]);
  const [orderEvents, setOrderEvents] = useState([]);
  const [otherEvents, setOtherEvents] = useState([]); 

  useEffect(() => {
    const hubUrl = getEventBridgeUrl();

    const conn = new signalR.HubConnectionBuilder()
      .withUrl(hubUrl, {
		  transport: signalR.HttpTransportType.WebSockets | signalR.HttpTransportType.LongPolling
		})
      .withAutomaticReconnect()
      .configureLogging(signalR.LogLevel.Information)
      .build();

    conn.onreconnecting((err) => {
      setStatus("Reconnecting");
      console.warn("SignalR reconnecting", err);
    });

    conn.onreconnected(() => {
      setStatus("Connected");
      console.info("SignalR reconnected");
    });

    conn.onclose(() => {
      setStatus("Disconnected");
      console.info("SignalR closed");
    });

    conn.on("ReceiveEvent", (topic, message) => {
      let parsed = message;
      try {
		  console.info("ReceiveEvent===" + message);
        parsed = JSON.parse(message);
      } catch { /* ignore if not JSON */ }

      if (topic === "users.created" || topic === "users.created.v1") {
        setUserEvents((s) => [{ topic, payload: parsed, raw: message, time: new Date() }, ...s]);
      } else if (topic === "orders.created" || topic === "orders.created.v1") {
        setOrderEvents((s) => [{ topic, payload: parsed, raw: message, time: new Date() }, ...s]);
      } else {
        setOtherEvents((s) => [{ topic, payload: parsed, raw: message, time: new Date() }, ...s]);
      }
    });

    async function start() {
      try {
        await conn.start();
        setConnection(conn);
        setStatus("Connected");
        console.info("SignalR Connected to", hubUrl);
      } catch (err) {
        setStatus("Disconnected");
        console.error("SignalR Connection error:", err);
        setTimeout(start, 3000);
      }
    }

    start();

    return () => {
      if (conn) {
        conn.stop().catch(() => {});
      }
    };
  }, []);

  return (
    <div style={{ padding: 20, fontFamily: "Segoe UI, Roboto, sans-serif" }}>
      <h2>EventBridge Live Events</h2>
      <div style={{ marginBottom: 12 }}>
        <strong>SignalR:</strong> <span>{status}</span>
      </div>
      <section style={{ display: "flex", gap: 16 }}>
        <div style={{ flex: 1 }}>
          <h3>Users Created</h3>
          {userEvents.length === 0 ? <div>No user events yet</div> : null}
          <ul>
            {userEvents.map((e, i) => (
              <li key={i} style={{ marginBottom: 8 }}>
                <div><small>{e.time.toLocaleTimeString()}</small></div>
                <pre style={{ whiteSpace: "pre-wrap", margin: 0 }}>{JSON.stringify(e.payload, null, 2)}</pre>
              </li>
            ))}
          </ul>
        </div>
        <div style={{ flex: 1 }}>
          <h3>Orders Created</h3>
          {orderEvents.length === 0 ? <div>No order events yet</div> : null}
          <ul>
            {orderEvents.map((e, i) => (
              <li key={i} style={{ marginBottom: 8 }}>
                <div><small>{e.time.toLocaleTimeString()}</small></div>
                <pre style={{ whiteSpace: "pre-wrap", margin: 0 }}>{JSON.stringify(e.payload, null, 2)}</pre>
              </li>
            ))}
          </ul>
        </div>
        <div style={{ flex: 1 }}>
          <h3>Other Events</h3>
          {otherEvents.length === 0 ? <div>No other events yet</div> : null}
          <ul>
            {otherEvents.map((e, i) => (
              <li key={i} style={{ marginBottom: 8 }}>
                <div><small>{e.time.toLocaleTimeString()}</small> <strong>{e.topic}</strong></div>
                <pre style={{ whiteSpace: "pre-wrap", margin: 0 }}>{JSON.stringify(e.payload, null, 2)}</pre>
              </li>
            ))}
          </ul>
        </div>
      </section>
    </div>
  );
}