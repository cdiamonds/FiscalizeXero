import { Component, OnInit, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { CommonModule } from '@angular/common';
import { ThemeService } from './core/services/theme/theme.service';
import { PwaService } from './core/services/pwa/pwa.service';
import { SessionExpiredModal } from './components/session-expired-modal/session-expired-modal';
import { InstallPrompt } from './components/install-prompt/install-prompt';
import { ToastContainer } from './components/toast-container/toast-container';
import { SessionKeepAliveService } from './core/services/session/session-keep-alive.service';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, CommonModule, SessionExpiredModal, InstallPrompt, ToastContainer],
  templateUrl: './app.html',
  styleUrl: './app.css'
})
export class App implements OnInit {
  private themeService = inject(ThemeService);
  private pwa = inject(PwaService);
  private keepAlive = inject(SessionKeepAliveService);

  ngOnInit(): void {
    this.themeService.init();
    this.pwa.init();
    this.keepAlive.start();
  }
}
