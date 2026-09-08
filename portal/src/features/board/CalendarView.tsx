import { ChevronLeft, ChevronRight } from 'lucide-react';
import React from 'react';
import { type State, type Task } from '../../domain/models';
import { type Locale } from '../../shared/i18n';

type CalendarViewProps = {
  year: number;
  month: number;
  locale: Locale;
  t: (value: unknown, ...values: unknown[]) => string;
  setMonth: React.Dispatch<React.SetStateAction<number>>;
  setYear: React.Dispatch<React.SetStateAction<number>>;
  filtered: Task[];
  setDraft: React.Dispatch<React.SetStateAction<Task | null>>;
  data: State;
};

export function CalendarView({
  year,
  month,
  locale,
  t,
  setMonth,
  setYear,
  filtered,
  setDraft,
  data,
}: CalendarViewProps) {
  return (
    <div className="calendar">
      <div className="calendar-heading">
        <h3>
          {new Date(year, month).toLocaleDateString(locale, {
            month: 'long',
            year: 'numeric',
          })}
        </h3>
        <div>
          <button
            aria-label={t('Previous month')}
            className="icon-button"
            onClick={() => {
              if (month === 0) {
                setMonth(11);
                setYear(year - 1);
              } else setMonth(month - 1);
            }}
          >
            <ChevronLeft size={18} />
          </button>
          <button
            aria-label={t('Next month')}
            className="icon-button"
            onClick={() => {
              if (month === 11) {
                setMonth(0);
                setYear(year + 1);
              } else setMonth(month + 1);
            }}
          >
            <ChevronRight size={18} />
          </button>
        </div>
      </div>
      <div className="calendar-grid">
        {['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'].map((d) => (
          <div className="weekday" key={d}>
            {d}
          </div>
        ))}
        {Array.from({ length: new Date(year, month, 1).getDay() }, (_, i) => (
          <div className="calendar-day muted" key={'empty' + i} />
        ))}
        {Array.from({ length: new Date(year, month + 1, 0).getDate() }, (_, i) => (
          <div
            key={i}
            className={`calendar-day ${i + 1 === new Date().getDate() && month === new Date().getMonth() && year === new Date().getFullYear() ? 'today' : ''}`}
          >
            <span>{i + 1}</span>
            {filtered
              .filter(
                (t) =>
                  t.due ===
                  `${year}-${String(month + 1).padStart(2, '0')}-${String(i + 1).padStart(2, '0')}`,
              )
              .map((t) => (
                <button
                  key={t.id}
                  onClick={() => setDraft(structuredClone(t))}
                  className="calendar-task"
                  style={{
                    backgroundColor:
                      (data.labels.find((label) => t.labels.includes(label.name))?.color ??
                        '#7490b0') + '20',
                  }}
                >
                  {t.title}
                </button>
              ))}
          </div>
        ))}
      </div>
    </div>
  );
}
