import { CommonModule } from '@angular/common';
import { Component, inject } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { SessionStore } from '../../core/state/session.store';

@Component({
  selector: 'app-shell-layout',
  standalone: true,
  imports: [CommonModule, RouterLink, RouterLinkActive, RouterOutlet],
  templateUrl: './shell-layout.component.html',
  styleUrl: './shell-layout.component.scss'
})
export class ShellLayoutComponent {
  private readonly router = inject(Router);
  protected readonly sessionStore = inject(SessionStore);

  protected async signOut(): Promise<void> {
    await this.sessionStore.signOut();
    await this.router.navigateByUrl('/login');
  }
}