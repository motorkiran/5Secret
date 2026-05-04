import { Component } from '@angular/core';

@Component({
  selector: 'app-redirect-placeholder',
  standalone: true,
  template: `
    <section class="page-shell placeholder-shell">
      <div class="surface-card placeholder-card">
        <p class="eyebrow">Secret Manager</p>
        <h1>Preparing the control plane.</h1>
      </div>
    </section>
  `,
  styles: `
    .placeholder-shell {
      display: grid;
      place-items: center;
    }

    .placeholder-card {
      width: min(28rem, 100%);
      padding: 2rem;
    }
  `
})
export class RedirectPlaceholderComponent {}