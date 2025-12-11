import React, { useEffect, useState } from "react";
import { HubConnectionBuilder } from "@microsoft/signalr";

function App() {
  const [events, setEvents] = useState([]);

  useEffect(() => {
    const conn = new HubConnectionBuilder()
      .withUrl("/eventhub")
      .withAutomaticReconnect()
      .build();

    conn.on("ReceiveEvent", (topic, payload) => {
      setEvents((e) => [{ topic, payload, ts: new Date().toISOString() }, ...e].slice(0, 50));
    });

    conn.start().catch((err) => console.error(err));
    return () => conn.stop();
  }, []);

  return (
    <div style={{ padding: 20 }}>
      <h2>Live Events</h2>
      <ul>
        {events.map((ev, i) => (
          <li key={i}><b>{ev.topic}</b> at {ev.ts} — <pre style={{display: "inline"}}>{ev.payload}</pre></li>
        ))}
      </ul>
    </div>
  );
}

export default App;
