import { createContext, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import pt from './pt-PT.json';
export type Locale = 'en-US' | 'pt-PT';
const dictionary: Record<string, string> = pt;
const templates = Object.entries(dictionary)
  .filter(([key]) => /\{\d+\}/.test(key))
  .map(([key, value]) => ({
    regex: new RegExp(
      '^' +
        key
          .split(/(\{\d+\})/)
          .map((part) =>
            /\{\d+\}/.test(part) ? '(.+?)' : part.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'),
          )
          .join('') +
        '$',
    ),
    value,
  }));
export function translate(locale: Locale, value: unknown, ...values: unknown[]): string {
  const key = String(value ?? '');
  let result = key;
  if (locale === 'pt-PT') {
    result = dictionary[key] ?? key;
    if (result === key && !values.length) {
      for (const template of templates) {
        const match = template.regex.exec(key);
        if (match) {
          result = template.value.replace(/\{(\d+)\}/g, (_, n) => match[Number(n) + 1] ?? '');
          break;
        }
      }
    }
  }
  return result
    .replace(/\{(\d+)\}/g, (token, n) =>
      values[Number(n)] === undefined ? token : String(values[Number(n)]),
    )
    .replaceAll('&nbsp;', '\u00a0');
}
const Context = createContext<{
  locale: Locale;
  setLocale: (locale: Locale) => void;
  t: (value: unknown, ...values: unknown[]) => string;
}>({ locale: 'en-US', setLocale: () => {}, t: (value) => String(value ?? '') });
export function I18nProvider({ children }: { children: ReactNode }) {
  const [locale, setLocale] = useState<Locale>(() => {
    try {
      return localStorage.getItem('kanbada-locale') === 'pt-PT' ? 'pt-PT' : 'en-US';
    } catch {
      return 'en-US';
    }
  });
  useEffect(() => {
    document.documentElement.lang = locale;
    try {
      localStorage.setItem('kanbada-locale', locale);
    } catch {
      /* Keep the session language. */
    }
  }, [locale]);
  const value = useMemo(
    () => ({
      locale,
      setLocale,
      t: (text: unknown, ...args: unknown[]) => translate(locale, text, ...args),
    }),
    [locale],
  );
  return <Context.Provider value={value}>{children}</Context.Provider>;
}
export const useI18n = () => useContext(Context);
