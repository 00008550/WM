/** @type {import('tailwindcss').Config} */
module.exports = {
  content: ['./src/**/*.{html,ts}'],
  theme: {
    extend: {
      colors: {
        // Control Room palette — all values live in styles.css as CSS variables
        // so a per-customer theme is a token swap, not a rebuild.
        ink: 'rgb(var(--wm-ink) / <alpha-value>)',
        surface: 'rgb(var(--wm-surface) / <alpha-value>)',
        raised: 'rgb(var(--wm-raised) / <alpha-value>)',
        line: 'rgb(var(--wm-line) / <alpha-value>)',
        text: 'rgb(var(--wm-text) / <alpha-value>)',
        muted: 'rgb(var(--wm-muted) / <alpha-value>)',
        pulse: 'rgb(var(--wm-pulse) / <alpha-value>)',   // electric mint — presence, "in"
        amber: 'rgb(var(--wm-amber) / <alpha-value>)',   // anomalies, warnings
        coral: 'rgb(var(--wm-coral) / <alpha-value>)',   // "out", errors
      },
      fontFamily: {
        display: ['"Space Grotesk"', 'sans-serif'],
        body: ['"IBM Plex Sans"', 'sans-serif'],
        mono: ['"IBM Plex Mono"', 'monospace'],
      },
      animation: {
        'pulse-ring': 'pulse-ring 2.4s cubic-bezier(0.4, 0, 0.2, 1) infinite',
        'ticker-in': 'ticker-in 320ms cubic-bezier(0.16, 1, 0.3, 1)',
      },
      keyframes: {
        'pulse-ring': {
          '0%': { boxShadow: '0 0 0 0 rgb(var(--wm-pulse) / 0.35)' },
          '70%': { boxShadow: '0 0 0 14px rgb(var(--wm-pulse) / 0)' },
          '100%': { boxShadow: '0 0 0 0 rgb(var(--wm-pulse) / 0)' },
        },
        'ticker-in': {
          from: { opacity: '0', transform: 'translateY(-8px) scale(0.98)' },
          to: { opacity: '1', transform: 'translateY(0) scale(1)' },
        },
      },
    },
  },
  plugins: [],
};
