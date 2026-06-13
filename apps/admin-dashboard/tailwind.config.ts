import type { Config } from "tailwindcss";

const config: Config = {
  content: ["./src/**/*.{ts,tsx}"],
  theme: {
    extend: {
      colors: {
        primary: "#0B1F3A",
        success: "#166534",
        warning: "#B45309",
        danger: "#B91C1C"
      }
    }
  },
  plugins: []
};

export default config;
