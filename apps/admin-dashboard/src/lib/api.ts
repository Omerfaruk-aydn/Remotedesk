import { z } from "zod";

const apiBaseUrl = process.env.NEXT_PUBLIC_API_URL ?? "http://localhost:8080";

export const dashboardStatsSchema = z.object({
  totalUsers: z.number(),
  onlineDevices: z.number(),
  activeSessions: z.number(),
  securityEventsLast24h: z.number()
});

export type DashboardStats = z.infer<typeof dashboardStatsSchema>;

export async function fetchDashboardStats(token: string): Promise<DashboardStats> {
  const response = await fetch(`${apiBaseUrl}/api/admin/overview`, {
    headers: { Authorization: `Bearer ${token}` },
    cache: "no-store"
  });
  if (!response.ok) {
    return { totalUsers: 0, onlineDevices: 0, activeSessions: 0, securityEventsLast24h: 0 };
  }
  return dashboardStatsSchema.parse(await response.json());
}
