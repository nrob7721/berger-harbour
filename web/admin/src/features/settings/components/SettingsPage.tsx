import { PageHeader } from '@/components/Layout/Layout';
import { AddonsSection } from './AddonsSection';
import { BlockedPeriodsSection } from './BlockedPeriodsSection';
import { EmailTemplatesSection } from './EmailTemplatesSection';
import { GeneralSection } from './GeneralSection';
import { SeasonsSection } from './SeasonsSection';
import styles from './Settings.module.css';

const SECTIONS = [
  { id: 'general', label: 'General' },
  { id: 'seasons', label: 'Seasons' },
  { id: 'blocked-periods', label: 'Blocked periods' },
  { id: 'add-ons', label: 'Add-ons' },
  { id: 'email-templates', label: 'Email templates' },
];

export function SettingsPage() {
  return (
    <div className={styles.page}>
      <PageHeader title="Settings" />
      <nav aria-label="Settings sections" className={styles.anchors}>
        {SECTIONS.map((s) => <a key={s.id} href={`#${s.id}`}>{s.label}</a>)}
      </nav>
      <GeneralSection />
      <SeasonsSection />
      <BlockedPeriodsSection />
      <AddonsSection />
      <EmailTemplatesSection />
    </div>
  );
}
