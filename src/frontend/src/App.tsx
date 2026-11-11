import { useEffect, useState } from "react";
import { apiGet } from "./lib/api.ts";

interface ServiceStatus {
  serviceId: string;
  state: string;
}

const SERVICES = ["api", "producer-a", "producer-b", "consumer-a", "consumer-b", "broker"];

// Skeleton tile grid; full panels and controls land in Task 5.
export default function App() {
  const [services, setServices] = useState<ServiceStatus[]>(
    SERVICES.map((serviceId) => ({ serviceId, state: "Down" })),
  );

  useEffect(() => {
    let live = true;
    const poll = async () => {
      try {
        const data = await apiGet<ServiceStatus[]>("/services");
        if (live) setServices(data);
      } catch {
        if (live) setServices(SERVICES.map((serviceId) => ({ serviceId, state: "Down" })));
      }
    };
    void poll();
    const id = setInterval(() => void poll(), 1000);
    return () => {
      live = false;
      clearInterval(id);
    };
  }, []);

  return (
    <main className="mx-auto max-w-4xl p-4">
      <h1 className="text-xl font-bold">EDD Demo</h1>
      <div className="mt-4 grid grid-cols-2 gap-2 sm:grid-cols-3">
        {services.map((s) => (
          <div key={s.serviceId} className="rounded border p-3">
            <div className="text-sm font-medium">{s.serviceId}</div>
            <div className="text-lg">{s.state}</div>
          </div>
        ))}
      </div>
    </main>
  );
}
