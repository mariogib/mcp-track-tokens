import { createContext, useContext, type ReactNode } from 'react';
import { useThemeState, type ThemeMode, type ThemePreference } from './themeState';

type ThemeContextValue = {
  theme: ThemeMode;
  preference: ThemePreference;
  toggleTheme: () => void;
  setTheme: (theme: ThemeMode) => void;
};

const ThemeContext = createContext<ThemeContextValue | null>(null);

export function ThemeProvider({ children }: { children: ReactNode }) {
  const value = useThemeState();
  return <ThemeContext.Provider value={value}>{children}</ThemeContext.Provider>;
}

export function useTheme(): ThemeContextValue {
  const ctx = useContext(ThemeContext);
  if (!ctx) {
    throw new Error('useTheme must be used within ThemeProvider');
  }
  return ctx;
}

export type { ThemeMode, ThemePreference };
