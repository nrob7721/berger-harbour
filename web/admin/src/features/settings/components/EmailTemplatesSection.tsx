import { useEffect, useId, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Button } from '@shared/components/Button/Button';
import { Field, inputClass } from '@shared/components/Field/Field';
import { useToast } from '@shared/components/Toast/Toast';
import { ApiError, errorMessage } from '@shared/lib/http';
import { Section } from '@/components/Section';
import { api, keys } from '@/lib/api';
import type { EmailTemplateKey } from '@/lib/types';
import styles from './Settings.module.css';

const LABELS: Record<EmailTemplateKey, string> = {
  BookingConfirmed: '#1 Booking confirmed (customer)',
  StaffNewOnlineBooking: '#2 New online booking (staff)',
  PaymentDue: '#4 Payment due (customer)',
  PaymentReceived: '#5 Payment received (customer)',
  PreHireInstructions: '#6 Pre-hire instructions (customer)',
  StaffBalanceOverdue: '#7 Overdue payment (staff)',
  StaffStandbySuperseded: '#8 Stand-by superseded (staff)',
  StaffAbandonedBooking: '#10 Unfinished online booking (staff)',
};

export function EmailTemplatesSection() {
  const templates = useQuery({ queryKey: keys.templates, queryFn: api.templates, staleTime: 60_000 });
  const [key, setKey] = useState<EmailTemplateKey>('BookingConfirmed');
  const [subject, setSubject] = useState('');
  const [body, setBody] = useState('');
  const base = useId();
  const queryClient = useQueryClient();
  const toast = useToast();
  const current = templates.data?.templates.find((t) => t.key === key);

  useEffect(() => {
    if (current) {
      setSubject(current.subject);
      setBody(current.htmlBody);
    }
  }, [current]);

  const save = useMutation({
    mutationFn: () => api.updateTemplate(key, subject, body, current?.version ?? 0),
    onSuccess: () => { void queryClient.invalidateQueries({ queryKey: keys.templates }); toast.show('Template saved.'); },
  });
  const test = useMutation({
    mutationFn: () => api.sendTestTemplate(key),
    onSuccess: () => toast.show('Test email sent to the staff alert address.'),
    onError: (e) => toast.show(errorMessage(e), 'error'),
  });
  const dirty = !!current && (subject !== current.subject || body !== current.htmlBody);
  const fieldError = (f: string) => (save.error instanceof ApiError ? save.error.fieldError(f) : undefined);

  return (
    <Section id="email-templates" title="Email templates" help="Cancellations and add-on changes are not emailed by the system; staff contact the customer.">
      <Field label="Template" htmlFor={`${base}-key`}>
        <select id={`${base}-key`} className={inputClass} value={key} onChange={(e) => setKey(e.target.value as EmailTemplateKey)} disabled={dirty || save.isPending}>
          {(Object.keys(LABELS) as EmailTemplateKey[]).map((k) => <option key={k} value={k}>{LABELS[k]}</option>)}
        </select>
      </Field>
      {dirty && <p className={styles.help}>Save or undo your changes before choosing another template.</p>}
      <div className={styles.templateGrid}>
        <fieldset className={styles.fieldset} disabled={save.isPending || !current}>
          <Field label="Subject" htmlFor={`${base}-subject`} error={fieldError('subject')}>
            <input id={`${base}-subject`} className={inputClass} value={subject} onChange={(e) => setSubject(e.target.value)} />
          </Field>
          <Field label="HTML body" htmlFor={`${base}-body`} error={fieldError('htmlBody')}>
            <textarea id={`${base}-body`} className={`${inputClass} ${styles.code}`} rows={16} value={body} onChange={(e) => setBody(e.target.value)} />
          </Field>
          <div className={styles.formActions}>
            {save.isError && !fieldError('subject') && !fieldError('htmlBody') && <span role="alert" className={styles.error}>{errorMessage(save.error)}</span>}
            <Button onClick={() => { setSubject(current!.subject); setBody(current!.htmlBody); }} disabled={!dirty}>Undo</Button>
            <Button onClick={() => test.mutate()} busy={test.isPending} busyLabel="Sending…" disabled={dirty}>Send test</Button>
            <Button variant="primary" onClick={() => save.mutate()} busy={save.isPending} busyLabel="Saving…" disabled={!dirty}>Save</Button>
          </div>
        </fieldset>
        <aside className={styles.placeholders} aria-label="Placeholders">
          <h3 className={styles.subheading}>Placeholders</h3>
          <ul>{templates.data?.placeholders.map((p) => <li key={p}><code>{`{{${p}}}`}</code></li>)}</ul>
        </aside>
      </div>
    </Section>
  );
}
