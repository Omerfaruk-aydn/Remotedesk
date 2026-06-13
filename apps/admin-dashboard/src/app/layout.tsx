import type { Metadata } from "next";
import "./globals.css";

export const metadata: Metadata = {
  title: "SecureRemoteDesk Admin",
  description: "Secure remote support administration dashboard"
};

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="en">
      <body>{children}</body>
    </html>
  );
}
