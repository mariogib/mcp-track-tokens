import { useId } from 'react';

type DateTimeFieldProps = {
  id?: string;
  label: string;
  value: string;
  onChange: (value: string) => void;
  required?: boolean;
  disabled?: boolean;
};

function normalizeTime(timePart: string): string {
  if (!timePart) return '';
  const [hours = '00', minutes = '00', seconds = '00'] = timePart.split(':');
  const pad = (n: string) => n.padStart(2, '0').slice(0, 2);
  return `${pad(hours || '0')}:${pad(minutes || '0')}:${pad(seconds || '0')}`;
}

function splitLocalDateTime(value: string): { date: string; time: string } {
  if (!value) {
    return { date: '', time: '' };
  }
  const [date = '', timePart = ''] = value.split('T');
  return { date, time: normalizeTime(timePart) };
}

function joinLocalDateTime(date: string, time: string): string {
  if (!date) return '';
  const normalized = normalizeTime(time);
  return `${date}T${normalized || '00:00:00'}`;
}

/** True when value is a complete local `YYYY-MM-DDTHH:mm:ss` timestamp. */
export function isCompleteLocalDateTime(value: string): boolean {
  if (!value) return false;
  const { date, time } = splitLocalDateTime(value);
  return Boolean(date && time && !Number.isNaN(new Date(value).getTime()));
}

function openPicker(input: HTMLInputElement | null) {
  if (!input) return;
  try {
    input.showPicker?.();
  } catch {
    // Some environments block showPicker outside a direct user gesture.
  }
}

/** Native date + time pickers bound as a local `YYYY-MM-DDTHH:mm:ss` value. */
export function DateTimeField({
  id,
  label,
  value,
  onChange,
  required = false,
  disabled = false,
}: DateTimeFieldProps) {
  const autoId = useId();
  const baseId = id ?? autoId;
  const dateId = `${baseId}-date`;
  const timeId = `${baseId}-time`;
  const { date, time } = splitLocalDateTime(value);

  return (
    <div className="field datetime-field">
      <span className="datetime-field-label" id={`${baseId}-label`}>
        {label}
        {required ? ' *' : ''}
      </span>
      <div className="datetime-field-inputs" role="group" aria-labelledby={`${baseId}-label`}>
        <input
          id={dateId}
          type="date"
          className="datetime-field-date"
          // Validate in the form submit handler so native tooltips do not silently block save.
          required={false}
          disabled={disabled}
          value={date}
          onChange={(e) => {
            const nextDate = e.target.value;
            if (!nextDate) {
              onChange('');
              return;
            }
            onChange(joinLocalDateTime(nextDate, time || '00:00:00'));
          }}
          onClick={(e) => openPicker(e.currentTarget)}
          aria-required={required}
        />
        <input
          id={timeId}
          type="time"
          step={1}
          className="datetime-field-time"
          required={false}
          disabled={disabled}
          value={time}
          onChange={(e) => {
            if (!date) {
              onChange('');
              return;
            }
            onChange(joinLocalDateTime(date, e.target.value || '00:00:00'));
          }}
          onClick={(e) => openPicker(e.currentTarget)}
          aria-required={required}
        />
      </div>
    </div>
  );
}
