// Cloudflare Turnstile, rendered explicitly into the details step.

interface TurnstileApi {
  render(container: HTMLElement, options: Record<string, unknown>): string;
  reset(widgetId?: string): void;
}

declare global {
  interface Window {
    turnstile?: TurnstileApi;
  }
}

const SCRIPT = 'https://challenges.cloudflare.com/turnstile/v0/api.js?render=explicit';

export class TurnstileWidget {
  private token: string | null = null;
  private widgetId: string | null = null;

  constructor(private readonly onChange: (token: string | null) => void) {}

  get value(): string | null {
    return this.token;
  }

  async render(container: HTMLElement): Promise<void> {
    if (this.widgetId) return;
    const siteKey = (import.meta.env.VITE_TURNSTILE_SITE_KEY as string | undefined) ?? '1x00000000000000000000AA';
    await loadScript();
    this.widgetId = window.turnstile!.render(container, {
      sitekey: siteKey,
      action: 'booking',
      'refresh-expired': 'auto',
      callback: (t: string) => this.set(t),
      'expired-callback': () => this.set(null),
      'error-callback': () => this.set(null),
    });
  }

  /** Tokens are single use: reset after each booking attempt. */
  reset(): void {
    this.set(null);
    if (this.widgetId) window.turnstile?.reset(this.widgetId);
  }

  private set(token: string | null) {
    this.token = token;
    this.onChange(token);
  }
}

let loading: Promise<void> | null = null;

function loadScript(): Promise<void> {
  loading ??= new Promise((resolve, reject) => {
    const script = document.createElement('script');
    script.src = SCRIPT;
    script.async = true;
    script.onload = () => resolve();
    script.onerror = () => {
      loading = null;
      reject(new Error('The security check could not load.'));
    };
    document.head.append(script);
  });
  return loading;
}
