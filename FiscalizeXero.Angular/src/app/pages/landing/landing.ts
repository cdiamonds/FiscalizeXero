import { Component, HostListener, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { FiscalizeLogo } from '../../components/branding/fiscalize-logo/fiscalize-logo';
import { RevealDirective } from '../../core/directives/reveal.directive';
import { AuthService } from '../../core/services/auth/auth.service';
// import { Dialog } from '../../components/dialog/dialog';

interface Feature {
  title: string;
  desc: string;
  iconBg: string;
  icon: string;
  delay: number;
  featured?: boolean;
}
interface Step {
  num: string;
  title: string;
  desc: string;
  gradient: string;
  icon: string;
}
/* interface Video {
  title: string;
  desc: string;
  duration: string;
  delay: number;
  thumb: string;
  url: string;
} */
interface Testimonial {
  name: string;
  role: string;
  company: string;
  initials: string;
  gradient: string;
  delay: number;
  text: string;
}

@Component({
  selector: 'app-landing',
  imports: [CommonModule, RouterLink, FiscalizeLogo, RevealDirective],
  template: `
    <div class="relative min-h-screen overflow-x-hidden bg-gradient-to-br from-slate-50 via-white to-brand-50/40 dark:from-slate-950 dark:via-slate-950 dark:to-black">
      <!-- Decorative blobs -->
      <div class="bg-blob teal" style="top: -160px; left: -120px; width: 520px; height: 520px;"></div>
      <div class="bg-blob emerald" style="top: 120px; right: -180px; width: 480px; height: 480px;"></div>
      <div class="bg-blob cyan" style="top: 720px; left: 30%; width: 420px; height: 420px;"></div>

      <!-- Header -->
      <header class="fixed top-0 left-0 right-0 z-50 transition-all duration-300"
              [class.border-b]="scrolled()"
              [class.border-slate-200\\/70]="scrolled()"
              [class.bg-white\\/80]="scrolled()"
              [class.shadow-sm]="scrolled()"
              [class.backdrop-blur-md]="scrolled()"
              [class.dark\\:bg-slate-950\\/70]="scrolled()"
              [class.dark\\:border-white\\/5]="scrolled()">
        <div class="container mx-auto px-4 lg:px-8">
          <div class="flex h-16 items-center justify-between lg:h-[4.5rem]">
            <app-fiscalize-logo></app-fiscalize-logo>

            <!-- Desktop nav -->
            <nav class="hidden items-center gap-8 md:flex">
              <a *ngFor="let link of navLinks" [href]="link.href"
                 class="group relative text-sm font-medium text-slate-600 transition-colors hover:text-slate-900 dark:text-slate-300 dark:hover:text-white">
                {{ link.label }}
                <span class="absolute -bottom-1 left-0 h-0.5 w-0 rounded-full bg-gradient-to-r from-primary-500 to-primary-600 transition-all duration-300 group-hover:w-full"></span>
              </a>
              @if (!isAuthenticated()) {
                <a href="https://fiscalize.co.zw" target="_blank" rel="noopener noreferrer"
                   class="group relative inline-flex items-center gap-1 text-sm font-medium text-primary-600 transition-colors hover:text-primary-700 dark:text-brand-400 dark:hover:text-brand-300">
                  Fiscalize
                  <svg class="h-3 w-3 transition-transform group-hover:translate-x-0.5 group-hover:-translate-y-0.5" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><path d="M18 13v6a2 2 0 01-2 2H5a2 2 0 01-2-2V8a2 2 0 012-2h6M15 3h6v6M10 14L21 3"/></svg>
                </a>
              }
            </nav>

            <!-- Desktop CTA -->
            <div class="hidden items-center gap-3 md:flex">
              @if (isAuthenticated()) {
                <a routerLink="/dashboard"
                   class="flex items-center gap-2 rounded-xl border border-slate-200/70 bg-white/70 px-3 py-1.5 text-sm font-medium text-slate-700 transition-all duration-200 hover:bg-white hover:scale-[1.02] dark:border-white/5 dark:bg-white/5 dark:text-slate-200 dark:hover:bg-white/10">
                  <span class="flex h-7 w-7 items-center justify-center rounded-full bg-gradient-to-br from-primary-500 to-primary-600 text-xs font-bold text-white shadow-sm">{{ initials() }}</span>
                  <span>Go to Dashboard</span>
                </a>
              } @else {
                <a routerLink="/login"
                   class="rounded-xl px-3 py-1.5 text-sm font-medium text-slate-700 transition-all duration-200 hover:bg-slate-100 hover:scale-[1.02] dark:text-slate-200 dark:hover:bg-white/5">
                  Sign in
                </a>
                <a href="https://fiscalize.co.zw/sign-up" target="_blank" rel="noopener noreferrer"
                   class="group inline-flex items-center gap-1 rounded-xl bg-gradient-to-r from-primary-500 to-primary-600 px-4 py-2 text-sm font-semibold text-white shadow-md shadow-primary-500/25 transition-all duration-200 hover:from-primary-600 hover:to-primary-700 hover:scale-[1.02] active:scale-[0.98]">
                  Create account
                  <svg class="h-3.5 w-3.5 transition-transform group-hover:translate-x-0.5" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><path d="M9 18l6-6-6-6"/></svg>
                </a>
              }
            </div>

            <!-- Mobile toggle -->
            <button type="button" (click)="mobileMenuOpen.set(!mobileMenuOpen())"
                    class="rounded-lg p-2 text-slate-700 transition-colors hover:bg-slate-100 dark:text-slate-200 dark:hover:bg-white/5 md:hidden"
                    aria-label="Toggle menu">
              <svg *ngIf="!mobileMenuOpen()" class="h-5 w-5" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M3 12h18M3 6h18M3 18h18"/></svg>
              <svg *ngIf="mobileMenuOpen()" class="h-5 w-5" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M18 6L6 18M6 6l12 12"/></svg>
            </button>
          </div>
        </div>

        <!-- Mobile menu -->
        <div *ngIf="mobileMenuOpen()" class="fade-in-down border-t border-slate-200/70 bg-white/95 backdrop-blur-xl dark:border-white/5 dark:bg-slate-950/95 md:hidden">
          <div class="container mx-auto space-y-1 px-4 py-4">
            <a *ngFor="let link of navLinks" [href]="link.href" (click)="mobileMenuOpen.set(false)"
               class="block rounded-lg px-3 py-2.5 text-sm font-medium text-slate-700 transition-colors hover:bg-slate-100 dark:text-slate-200 dark:hover:bg-white/5">
              {{ link.label }}
            </a>
            @if (!isAuthenticated()) {
              <a href="https://fiscalize.co.zw" target="_blank" rel="noopener noreferrer" (click)="mobileMenuOpen.set(false)"
                 class="flex items-center gap-1.5 rounded-lg px-3 py-2.5 text-sm font-medium text-primary-600 transition-colors hover:bg-slate-100 dark:text-brand-400 dark:hover:bg-white/5">
                Fiscalize
                <svg class="h-3 w-3" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><path d="M18 13v6a2 2 0 01-2 2H5a2 2 0 01-2-2V8a2 2 0 012-2h6M15 3h6v6M10 14L21 3"/></svg>
              </a>
            }
            <div class="mt-3 flex gap-3 border-t border-slate-200/70 pt-3 dark:border-white/5">
              @if (isAuthenticated()) {
                <a routerLink="/dashboard" (click)="mobileMenuOpen.set(false)" class="flex flex-1 items-center justify-center gap-2 rounded-xl border border-slate-200 px-3 py-2 text-center text-sm font-medium text-slate-700 dark:border-white/10 dark:text-slate-200">
                  <span class="flex h-6 w-6 items-center justify-center rounded-full bg-gradient-to-br from-primary-500 to-primary-600 text-[10px] font-bold text-white">{{ initials() }}</span>
                  Dashboard
                </a>
              } @else {
                <a routerLink="/login" (click)="mobileMenuOpen.set(false)" class="flex-1 rounded-xl border border-slate-200 px-3 py-2 text-center text-sm font-medium text-slate-700 dark:border-white/10 dark:text-slate-200">Sign in</a>
                <a href="https://fiscalize.co.zw/sign-up" target="_blank" rel="noopener noreferrer" (click)="mobileMenuOpen.set(false)" class="flex-1 rounded-xl bg-gradient-to-r from-primary-500 to-primary-600 px-3 py-2 text-center text-sm font-semibold text-white">Create account</a>
              }
            </div>
          </div>
        </div>
      </header>

      <!-- Hero -->
      <section class="relative z-10 mx-auto max-w-7xl px-4 pb-20 pt-32 sm:px-6 lg:px-8 lg:pt-40">
        <div class="mx-auto max-w-3xl text-center">
          <span class="fade-in-down inline-flex items-center gap-2 rounded-full border border-brand-200/70 bg-brand-50/70 px-3 py-1 text-xs font-semibold uppercase tracking-wider text-primary-700 backdrop-blur-sm dark:border-primary-500/30 dark:bg-primary-500/10 dark:text-brand-300">
            <span class="relative flex h-2 w-2">
              <span class="absolute inline-flex h-full w-full animate-ping rounded-full bg-brand-400 opacity-60"></span>
              <span class="relative inline-flex h-2 w-2 rounded-full bg-primary-500"></span>
            </span>
            ZIMRA fiscalisation for Xero
          </span>

          <h1 class="mt-6 text-4xl font-bold leading-tight tracking-tight text-slate-900 dark:text-white sm:text-5xl lg:text-6xl">
            <span class="block fade-in-down" style="animation-delay: 0.05s">Bring ZIMRA compliance</span>
            <span class="block gradient-text" style="animation-delay: 0.15s">into Xero.</span>
          </h1>

          <p class="mx-auto mt-6 max-w-2xl text-lg text-slate-600 dark:text-slate-300 fade-in-down" style="animation-delay: 0.25s">
            Connect your Xero organisation and every approved invoice and credit note is fiscalised with ZIMRA, with the fiscal result written back onto the document in Xero.
          </p>

          <div class="mt-10 flex flex-col items-center justify-center gap-3 sm:flex-row fade-in-down" style="animation-delay: 0.35s">
            <a routerLink="/login"
               class="group inline-flex items-center justify-center gap-2 rounded-xl bg-gradient-to-r from-primary-500 to-primary-600 px-7 py-3.5 text-base font-semibold text-white shadow-xl shadow-primary-500/30 transition-all duration-200 hover:from-primary-600 hover:to-primary-700 hover:scale-[1.02] active:scale-[0.98]">
              Sign in to get started
              <svg class="h-4 w-4 transition-transform group-hover:translate-x-0.5" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><path d="M5 12h14M13 5l7 7-7 7"/></svg>
            </a>
            <a href="#how-it-works"
               class="inline-flex items-center justify-center rounded-xl border border-slate-200 bg-white/70 px-7 py-3.5 text-base font-semibold text-slate-700 backdrop-blur-sm transition-all duration-200 hover:bg-white hover:scale-[1.02] active:scale-[0.98] dark:border-white/10 dark:bg-white/5 dark:text-slate-200 dark:hover:bg-white/10">
              See how it works
            </a>
          </div>
          <p class="mt-4 text-sm text-slate-500 fade-in-down dark:text-slate-400" style="animation-delay: 0.40s">
            Works with your Fiscalize account. Don't have one?
            <a href="https://fiscalize.co.zw/sign-up" target="_blank" rel="noopener noreferrer" class="font-semibold text-primary-600 hover:text-primary-700 dark:text-brand-400">Sign up free →</a>
          </p>

          <!-- Stat strip -->
          <div class="fade-in-down mx-auto mt-16 max-w-3xl" style="animation-delay: 0.45s">
            <div class="grid grid-cols-2 gap-4 rounded-2xl border border-slate-200/70 bg-white/70 p-2 backdrop-blur-md md:grid-cols-4 dark:border-white/10 dark:bg-slate-900/50">
              <div *ngFor="let s of stats" class="rounded-xl px-4 py-4 transition-colors hover:bg-slate-50/80 dark:hover:bg-white/5">
                <div class="text-2xl font-bold tabular-nums text-slate-900 dark:text-white lg:text-3xl">{{ s.value }}</div>
                <div class="mt-0.5 text-[11px] font-semibold uppercase tracking-wider text-slate-500 dark:text-slate-400">{{ s.label }}</div>
              </div>
            </div>
          </div>
        </div>
      </section>

      <!-- Features -->
      <section id="features" class="relative z-10 mx-auto max-w-7xl px-4 py-20 sm:px-6 lg:px-8 lg:py-28">
        <div class="mx-auto max-w-2xl text-center" appReveal>
          <div class="mb-5 inline-flex items-center gap-2 rounded-full border border-brand-200/70 bg-brand-50/70 px-3 py-1 text-xs font-semibold uppercase tracking-wider text-primary-700 dark:border-primary-500/30 dark:bg-primary-500/10 dark:text-brand-300">
            <svg class="h-3 w-3" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><path d="M9.937 15.5A2 2 0 008.5 14.063l-6.135-1.582a.5.5 0 010-.962L8.5 9.936A2 2 0 009.937 8.5l1.582-6.135a.5.5 0 01.963 0L14.063 8.5A2 2 0 0015.5 9.937l6.135 1.581a.5.5 0 010 .964L15.5 14.063a2 2 0 00-1.437 1.437l-1.582 6.135a.5.5 0 01-.963 0z"/></svg>
            Features
          </div>
          <h2 class="text-3xl font-bold tracking-tight text-slate-900 dark:text-white sm:text-4xl">Why choose Fiscalize?</h2>
          <p class="mt-3 text-base text-slate-600 dark:text-slate-300">
            Powerful tools designed specifically for Zimbabwean businesses to automate ZIMRA compliance.
          </p>
        </div>

        <div class="mt-12 grid gap-6 md:grid-cols-2 lg:grid-cols-3">
          <div *ngFor="let f of features; let i = index"
               appReveal
               [revealDelay]="i * 80"
               class="card-hover group relative overflow-hidden rounded-2xl border p-6 backdrop-blur-md transition-all"
               [ngClass]="f.featured
                 ? 'border-brand-300/60 bg-gradient-to-br from-brand-50/80 to-brand-50/60 dark:border-primary-500/30 dark:from-primary-500/10 dark:to-primary-600/5'
                 : 'border-slate-200/70 bg-white/80 dark:border-white/10 dark:bg-slate-900/60'">

            <span *ngIf="f.featured" class="absolute right-5 top-5 rounded-full border border-brand-300/60 bg-brand-100 px-2.5 py-1 text-[10px] font-semibold uppercase tracking-wider text-primary-700 dark:border-primary-500/30 dark:bg-primary-500/15 dark:text-brand-300">
              Certified
            </span>

            <div class="flex h-12 w-12 items-center justify-center rounded-2xl shadow-lg transition-transform duration-200 group-hover:scale-110"
                 [class]="'bg-gradient-to-br ' + f.iconBg + ' shadow-primary-500/20'">
              <svg class="h-6 w-6 text-white" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path [attr.d]="f.icon"></path></svg>
            </div>
            <h3 class="mt-5 text-lg font-semibold text-slate-900 dark:text-white">{{ f.title }}</h3>
            <p class="mt-2 text-sm leading-relaxed text-slate-600 dark:text-slate-300">{{ f.desc }}</p>
          </div>
        </div>
      </section>

      <!-- How it works -->
      <section id="how-it-works" class="relative z-10 mx-auto max-w-7xl px-4 py-20 sm:px-6 lg:px-8 lg:py-28">
        <div class="rounded-3xl border border-slate-200/70 bg-white/70 p-8 backdrop-blur-md dark:border-white/10 dark:bg-slate-900/50 lg:p-12" appReveal>
          <div class="mx-auto max-w-2xl text-center">
            <div class="mb-5 inline-flex items-center gap-2 rounded-full border border-brand-200/70 bg-brand-50/70 px-3 py-1 text-xs font-semibold uppercase tracking-wider text-primary-700 dark:border-primary-500/30 dark:bg-primary-500/10 dark:text-brand-300">
              <svg class="h-3 w-3" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><polygon points="13 2 3 14 12 14 11 22 21 10 12 10 13 2"/></svg>
              Process
            </div>
            <h2 class="text-3xl font-bold tracking-tight text-slate-900 dark:text-white sm:text-4xl">Get started in 3 steps</h2>
            <p class="mt-3 text-base text-slate-600 dark:text-slate-300">From setup to full ZIMRA compliance in minutes, not days.</p>
          </div>

          <div class="relative mt-12 grid gap-8 lg:grid-cols-3">
            <div class="absolute left-[calc(33.33%-1rem)] right-[calc(33.33%-1rem)] top-12 hidden h-px lg:block"
                 style="background: linear-gradient(to right, rgba(6,182,212,0.4), rgba(6,182,212,0.9), rgba(6,182,212,0.4));"></div>

            <div *ngFor="let step of steps; let i = index"
                 appReveal
                 [revealDelay]="i * 100"
                 class="flex flex-col items-center text-center">
              <div class="group relative">
                <div class="flex h-24 w-24 items-center justify-center rounded-3xl bg-gradient-to-br from-primary-500 to-primary-600 shadow-xl shadow-primary-500/30 transition-transform duration-300 group-hover:scale-105">
                  <svg class="h-10 w-10 text-white" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path [attr.d]="step.icon"></path></svg>
                </div>
                <div class="absolute -right-2 -top-2 flex h-8 w-8 items-center justify-center rounded-full border-2 border-slate-200 bg-white text-xs font-bold text-slate-500 shadow-sm dark:border-white/10 dark:bg-slate-900 dark:text-slate-300">
                  {{ step.num }}
                </div>
              </div>
              <h3 class="mt-6 text-lg font-semibold text-slate-900 dark:text-white">{{ step.title }}</h3>
              <p class="mt-2 max-w-xs text-sm leading-relaxed text-slate-600 dark:text-slate-300">{{ step.desc }}</p>
            </div>
          </div>

          <div class="mt-12 flex justify-center" appReveal [revealDelay]="450">
            <a routerLink="/register-client"
               class="group inline-flex items-center gap-2 rounded-xl bg-gradient-to-r from-primary-500 to-primary-600 px-6 py-3 text-base font-semibold text-white shadow-lg shadow-primary-500/25 transition-all duration-200 hover:from-primary-600 hover:to-primary-700 hover:scale-[1.02] active:scale-[0.98]">
              View detailed setup guide
              <svg class="h-4 w-4 transition-transform group-hover:translate-x-1" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><path d="M5 12h14M13 5l7 7-7 7"/></svg>
            </a>
          </div>
        </div>
      </section>

      <!-- Tutorials (commented out until videos are ready) -->
      <!--
      <section class="relative z-10 mx-auto max-w-7xl px-4 py-20 sm:px-6 lg:px-8 lg:py-28">
        <div class="mx-auto max-w-2xl text-center" appReveal>
          <div class="mb-5 inline-flex items-center gap-2 rounded-full border border-brand-200/70 bg-brand-50/70 px-3 py-1 text-xs font-semibold uppercase tracking-wider text-primary-700 dark:border-primary-500/30 dark:bg-primary-500/10 dark:text-brand-300">
            <svg class="h-3 w-3" viewBox="0 0 24 24" fill="currentColor"><polygon points="5 3 19 12 5 21 5 3"/></svg>
            Tutorials
          </div>
          <h2 class="text-3xl font-bold tracking-tight text-slate-900 dark:text-white sm:text-4xl">See Fiscalize in action</h2>
          <p class="mt-3 text-base text-slate-600 dark:text-slate-300">Quick tutorials to help you master ZIMRA compliance with Fiscalize.</p>
        </div>

        <div class="mx-auto mt-12 grid max-w-6xl gap-5 md:grid-cols-2">
          <button *ngFor="let v of videos; let i = index"
                  type="button"
                  (click)="openVideo(v)"
                  appReveal
                  [revealDelay]="i * 80"
                  class="card-hover group cursor-pointer overflow-hidden rounded-2xl border border-slate-200/70 bg-white/80 text-left backdrop-blur-md transition-all dark:border-white/10 dark:bg-slate-900/60">
            <div class="relative aspect-video overflow-hidden bg-gradient-to-br from-slate-100 to-slate-200 dark:from-slate-800 dark:to-slate-900">
              <img [src]="v.thumb" [alt]="v.title" loading="lazy"
                   class="h-full w-full object-cover opacity-70 transition-transform duration-500 group-hover:scale-105 group-hover:opacity-90" />
              <div class="absolute inset-0 bg-gradient-to-t from-slate-900/60 via-slate-900/10 to-transparent"></div>
              <div class="absolute inset-0 flex items-center justify-center">
                <div class="relative">
                  <div class="absolute inset-0 scale-150 rounded-full bg-brand-400/30 opacity-0 blur-xl transition-all duration-300 group-hover:opacity-100"></div>
                  <div class="relative flex h-14 w-14 items-center justify-center rounded-full bg-gradient-to-br from-primary-500 to-primary-600 shadow-2xl shadow-primary-500/40 transition-transform duration-300 group-hover:scale-110">
                    <svg class="ml-0.5 h-6 w-6 text-white" viewBox="0 0 24 24" fill="currentColor"><polygon points="5 3 19 12 5 21 5 3"/></svg>
                  </div>
                </div>
              </div>
              <div class="absolute bottom-3 right-3 rounded-full bg-slate-900/80 px-2.5 py-1 text-xs font-medium text-white backdrop-blur-sm">{{ v.duration }}</div>
              <div class="absolute left-3 top-3 rounded-full bg-gradient-to-r from-primary-500 to-primary-600 px-2.5 py-1 text-xs font-semibold text-white shadow-md shadow-primary-500/30">Tutorial {{ i + 1 }}</div>
            </div>
            <div class="p-5">
              <h3 class="text-base font-semibold leading-snug text-slate-900 transition-colors group-hover:text-primary-600 dark:text-white dark:group-hover:text-brand-400">{{ v.title }}</h3>
              <p class="mt-1.5 text-sm leading-relaxed text-slate-600 dark:text-slate-300">{{ v.desc }}</p>
            </div>
          </button>
        </div>
      </section>
      -->

      <!-- Testimonials -->
      <section class="relative z-10 mx-auto max-w-7xl px-4 py-20 sm:px-6 lg:px-8 lg:py-28">
        <div class="mx-auto max-w-2xl text-center" appReveal>
          <div class="mb-5 inline-flex items-center gap-2 rounded-full border border-amber-200/70 bg-amber-50/70 px-3 py-1 text-xs font-semibold uppercase tracking-wider text-amber-700 dark:border-amber-500/30 dark:bg-amber-500/10 dark:text-amber-300">
            <svg class="h-3 w-3 fill-amber-500" viewBox="0 0 24 24"><polygon points="12 2 15.09 8.26 22 9.27 17 14.14 18.18 21.02 12 17.77 5.82 21.02 7 14.14 2 9.27 8.91 8.26 12 2"/></svg>
            Reviews
          </div>
          <h2 class="text-3xl font-bold tracking-tight text-slate-900 dark:text-white sm:text-4xl">Trusted across Zimbabwe</h2>
          <p class="mt-3 text-base text-slate-600 dark:text-slate-300">Hear from businesses that transformed their compliance workflow.</p>
        </div>

        <div class="mx-auto mt-12 grid max-w-6xl gap-5 md:grid-cols-3">
          <div *ngFor="let t of testimonials; let i = index"
               appReveal
               [revealDelay]="i * 100"
               class="card-hover flex flex-col rounded-2xl border border-slate-200/70 bg-white/80 p-6 backdrop-blur-md dark:border-white/10 dark:bg-slate-900/60">
            <div class="mb-5 flex gap-1">
              <svg *ngFor="let _ of stars" class="h-4 w-4 fill-amber-500" viewBox="0 0 24 24"><polygon points="12 2 15.09 8.26 22 9.27 17 14.14 18.18 21.02 12 17.77 5.82 21.02 7 14.14 2 9.27 8.91 8.26 12 2"/></svg>
            </div>
            <p class="mb-6 flex-1 text-sm leading-relaxed text-slate-600 dark:text-slate-300">&ldquo;{{ t.text }}&rdquo;</p>
            <div class="flex items-center gap-3.5">
              <div class="flex h-10 w-10 flex-shrink-0 items-center justify-center rounded-full bg-gradient-to-br from-primary-500 to-primary-600 text-sm font-semibold text-white shadow-md shadow-primary-500/30">{{ t.initials }}</div>
              <div>
                <p class="text-sm font-semibold leading-tight text-slate-900 dark:text-white">{{ t.name }}</p>
                <p class="mt-0.5 text-xs text-slate-500 dark:text-slate-400">{{ t.role }}, {{ t.company }}</p>
              </div>
            </div>
          </div>
        </div>
      </section>

      <!-- Pricing -->
      <section id="pricing" class="relative z-10 mx-auto max-w-7xl px-4 py-20 sm:px-6 lg:px-8 lg:py-28">
        <div class="mx-auto max-w-2xl text-center" appReveal>
          <div class="mb-5 inline-flex items-center gap-2 rounded-full border border-brand-200/70 bg-brand-50/70 px-3 py-1 text-xs font-semibold uppercase tracking-wider text-primary-700 dark:border-primary-500/30 dark:bg-primary-500/10 dark:text-brand-300">
            <svg class="h-3 w-3" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><path d="M12 2v20M17 5H9.5a3.5 3.5 0 000 7h5a3.5 3.5 0 010 7H6"/></svg>
            Pricing
          </div>
          <h2 class="text-3xl font-bold tracking-tight text-slate-900 dark:text-white sm:text-4xl">Simple, transparent pricing</h2>
          <p class="mt-3 text-base text-slate-600 dark:text-slate-300">Fiscalize for Xero is included as part of the Fiscalize platform.</p>
        </div>

        <div class="mt-12 flex justify-center">
          <div appReveal class="card-hover relative w-full max-w-md overflow-hidden rounded-3xl border-2 border-brand-300/60 bg-gradient-to-br from-brand-50/80 to-brand-50/60 p-8 backdrop-blur-md dark:border-primary-500/30 dark:from-primary-500/10 dark:to-primary-600/5">
            <div class="absolute right-5 top-5 rounded-full border border-brand-300/60 bg-brand-100 px-2.5 py-1 text-[10px] font-semibold uppercase tracking-wider text-primary-700 dark:border-primary-500/30 dark:bg-primary-500/15 dark:text-brand-300">Included in Fiscalize</div>

            <div class="flex items-center gap-3">
              <div class="flex h-12 w-12 items-center justify-center rounded-2xl bg-gradient-to-br from-primary-500 to-primary-600 shadow-lg shadow-primary-500/30">
                <svg class="h-6 w-6 text-white" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M12 2v20M17 5H9.5a3.5 3.5 0 000 7h5a3.5 3.5 0 010 7H6"/></svg>
              </div>
              <div>
                <h3 class="text-xl font-bold text-slate-900 dark:text-white">Free</h3>
                <p class="text-sm text-slate-500 dark:text-slate-400">Part of the Fiscalize Plan</p>
              </div>
            </div>

            <div class="mt-6">
              <span class="text-5xl font-extrabold text-slate-900 dark:text-white">$0</span>
              <span class="ml-2 text-base text-slate-500 dark:text-slate-400">/ month</span>
            </div>

            <ul class="mt-8 space-y-3">
              <li *ngFor="let item of pricingFeatures" class="flex items-start gap-3">
                <svg class="mt-0.5 h-5 w-5 flex-shrink-0 text-primary-500" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><path d="M20 6L9 17l-5-5"/></svg>
                <span class="text-sm text-slate-700 dark:text-slate-300">{{ item }}</span>
              </li>
            </ul>

            <a href="https://fiscalize.co.zw/sign-up" target="_blank" rel="noopener noreferrer"
               class="mt-8 group flex w-full items-center justify-center gap-2 rounded-xl bg-gradient-to-r from-primary-500 to-primary-600 px-6 py-3.5 text-base font-semibold text-white shadow-lg shadow-primary-500/25 transition-all duration-200 hover:from-primary-600 hover:to-primary-700 hover:scale-[1.02] active:scale-[0.98]">
              Get started free
              <svg class="h-4 w-4 transition-transform group-hover:translate-x-0.5" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><path d="M5 12h14M13 5l7 7-7 7"/></svg>
            </a>
          </div>
        </div>
      </section>

      <!-- How it fits with Xero -->
      <section id="compatibility" class="relative z-10 mx-auto max-w-7xl px-4 py-20 sm:px-6 lg:px-8 lg:py-28">
        <div class="mx-auto max-w-2xl text-center" appReveal>
          <div class="mb-5 inline-flex items-center gap-2 rounded-full border border-brand-200/70 bg-brand-50/70 px-3 py-1 text-xs font-semibold uppercase tracking-wider text-primary-700 dark:border-primary-500/30 dark:bg-primary-500/10 dark:text-brand-300">
            <svg class="h-3 w-3" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><path d="M9 12l2 2 4-4m5.618-4.016A11.955 11.955 0 0112 2.944a11.955 11.955 0 01-8.618 3.04A12.02 12.02 0 003 9c0 5.591 3.824 10.29 9 11.622 5.176-1.332 9-6.03 9-11.622 0-1.042-.133-2.052-.382-3.016z"/></svg>
            How it fits
          </div>
          <h2 class="text-3xl font-bold tracking-tight text-slate-900 dark:text-white sm:text-4xl">You keep working in Xero</h2>
          <p class="mt-3 text-base text-slate-600 dark:text-slate-300">
            Raise and approve invoices in Xero as you do today. Fiscalize reads the approved documents, sends them to ZIMRA through your fiscal device, and records the result back on each one.
          </p>
        </div>

        <div class="mt-12 grid gap-6 lg:grid-cols-2" appReveal>

          <!-- In Xero -->
          <div class="card-hover rounded-2xl border border-slate-200/70 bg-white/80 p-8 backdrop-blur-md dark:border-white/10 dark:bg-slate-900/60">
            <div class="flex items-center gap-3">
              <div class="flex h-11 w-11 items-center justify-center rounded-xl bg-gradient-to-br from-slate-500 to-slate-600 shadow-md">
                <svg class="h-5 w-5 text-white" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M12 2v20M17 5H9.5a3.5 3.5 0 000 7h5a3.5 3.5 0 010 7H6"/></svg>
              </div>
              <div>
                <h3 class="text-lg font-bold text-slate-900 dark:text-white">What stays in Xero</h3>
                <p class="text-xs text-slate-500 dark:text-slate-400">Your books, unchanged</p>
              </div>
            </div>
            <ul class="mt-6 space-y-3">
              <li *ngFor="let item of freePlanItems" class="flex items-start gap-3">
                <svg class="mt-0.5 h-4 w-4 flex-shrink-0" [class]="item.check ? 'text-primary-500' : 'text-slate-300 dark:text-slate-600'" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round">
                  <path *ngIf="item.check" d="M20 6L9 17l-5-5"/>
                  <path *ngIf="!item.check" d="M18 6L6 18M6 6l12 12"/>
                </svg>
                <span class="text-sm leading-relaxed" [class]="item.check ? 'text-slate-700 dark:text-slate-300' : 'text-slate-400 dark:text-slate-600'">{{ item.label }}</span>
              </li>
            </ul>
            <div class="mt-6 rounded-xl border border-amber-200/70 bg-amber-50/60 px-4 py-3 text-xs leading-relaxed text-amber-700 dark:border-amber-500/20 dark:bg-amber-500/10 dark:text-amber-400">
              Fiscalize <strong class="font-semibold">never changes amounts, dates or lines</strong> on your Xero documents. It only adds the fiscal result.
            </div>
          </div>

          <!-- In Fiscalize -->
          <div class="card-hover relative overflow-hidden rounded-2xl border border-brand-300/60 bg-gradient-to-br from-brand-50/80 to-brand-50/60 p-8 backdrop-blur-md dark:border-primary-500/30 dark:from-primary-500/10 dark:to-primary-600/5">
            <div class="flex items-center gap-3">
              <div class="flex h-11 w-11 items-center justify-center rounded-xl bg-gradient-to-br from-primary-500 to-primary-600 shadow-md shadow-primary-500/30">
                <svg class="h-5 w-5 text-white" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M12 2v20M17 5H9.5a3.5 3.5 0 000 7h5a3.5 3.5 0 010 7H6"/></svg>
              </div>
              <div>
                <h3 class="text-lg font-bold text-slate-900 dark:text-white">What Fiscalize adds</h3>
                <p class="text-xs text-slate-500 dark:text-slate-400">The ZIMRA part, done for you</p>
              </div>
            </div>
            <ul class="mt-6 space-y-3">
              <li *ngFor="let item of paidPlanItems" class="flex items-start gap-3">
                <svg class="mt-0.5 h-4 w-4 flex-shrink-0 text-primary-500" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><path d="M20 6L9 17l-5-5"/></svg>
                <span class="text-sm leading-relaxed text-slate-700 dark:text-slate-300">{{ item }}</span>
              </li>
            </ul>
            <div class="mt-6 rounded-xl border border-brand-200/70 bg-white/60 px-4 py-3 text-xs leading-relaxed text-primary-700 dark:border-primary-500/20 dark:bg-primary-500/10 dark:text-brand-300">
              A customer's TIN and VAT number can live <strong class="font-semibold">on the Xero contact</strong> or be typed in Fiscalize; either way they reach ZIMRA.
            </div>
          </div>
        </div>

        <!-- Original invoice note -->
        <div class="mt-6 rounded-2xl border border-slate-200/70 bg-white/70 px-6 py-5 backdrop-blur-md dark:border-white/10 dark:bg-slate-900/50" appReveal [revealDelay]="200">
          <div class="flex items-start gap-4">
            <div class="flex h-9 w-9 flex-shrink-0 items-center justify-center rounded-xl bg-gradient-to-br from-primary-500 to-primary-600 shadow-md shadow-primary-500/20">
              <svg class="h-4 w-4 text-white" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><path d="M4 4v5h.582m15.356 2A8.001 8.001 0 004.582 9m0 0H9m11 11v-5h-.581m0 0a8.003 8.003 0 01-15.357-2m15.357 2H15"/></svg>
            </div>
            <div>
              <p class="text-sm font-semibold text-slate-900 dark:text-white">Credit notes follow their invoice</p>
              <p class="mt-1 text-sm leading-relaxed text-slate-500 dark:text-slate-400">
                ZIMRA credits a specific fiscal receipt. Allocate the credit note to its invoice in Xero and Fiscalize uses that link; you can also choose the invoice yourself.
              </p>
            </div>
          </div>
        </div>
      </section>

      <!-- Services -->
      <section id="services" class="relative z-10 mx-auto max-w-7xl px-4 py-20 sm:px-6 lg:px-8 lg:py-28">
        <div class="rounded-3xl border border-slate-200/70 bg-white/70 p-8 backdrop-blur-md dark:border-white/10 dark:bg-slate-900/50 lg:p-12" appReveal>
          <div class="mx-auto max-w-2xl text-center">
            <div class="mb-5 inline-flex items-center gap-2 rounded-full border border-brand-200/70 bg-brand-50/70 px-3 py-1 text-xs font-semibold uppercase tracking-wider text-primary-700 dark:border-primary-500/30 dark:bg-primary-500/10 dark:text-brand-300">
              <svg class="h-3 w-3" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><path d="M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/><path d="M22 21v-2a4 4 0 0 0-3-3.87M16 3.13a4 4 0 0 1 0 7.75"/></svg>
              Expert Services
            </div>
            <h2 class="text-3xl font-bold tracking-tight text-slate-900 dark:text-white sm:text-4xl">Need Xero implementation, customization, or support?</h2>
            <p class="mt-3 text-base text-slate-600 dark:text-slate-300">Our expert team is ready to help you get the most out of Xero and Fiscalize — from setup to custom workflows.</p>
          </div>

          <div class="mt-12 grid gap-6 md:grid-cols-3">
            <div *ngFor="let svc of services; let i = index"
                 appReveal
                 [revealDelay]="i * 100"
                 class="card-hover group flex flex-col rounded-2xl border border-slate-200/70 bg-white/80 p-6 backdrop-blur-md dark:border-white/10 dark:bg-slate-900/60">
              <div class="flex h-12 w-12 items-center justify-center rounded-2xl shadow-lg transition-transform duration-200 group-hover:scale-110"
                   [class]="'bg-gradient-to-br ' + svc.iconBg + ' shadow-primary-500/20'">
                <svg class="h-6 w-6 text-white" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path [attr.d]="svc.icon"></path></svg>
              </div>
              <h3 class="mt-5 text-lg font-semibold text-slate-900 dark:text-white">{{ svc.title }}</h3>
              <p class="mt-2 flex-1 text-sm leading-relaxed text-slate-600 dark:text-slate-300">{{ svc.desc }}</p>
              <ul class="mt-4 space-y-2">
                <li *ngFor="let point of svc.points" class="flex items-start gap-2 text-sm text-slate-600 dark:text-slate-300">
                  <svg class="mt-0.5 h-4 w-4 flex-shrink-0 text-primary-500" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><path d="M20 6L9 17l-5-5"/></svg>
                  {{ point }}
                </li>
              </ul>
            </div>
          </div>

          <div class="mt-12 flex justify-center" appReveal [revealDelay]="400">
            <a href="mailto:support@fiscalize.co.zw"
               class="group inline-flex items-center gap-2 rounded-xl bg-gradient-to-r from-primary-500 to-primary-600 px-6 py-3 text-base font-semibold text-white shadow-lg shadow-primary-500/25 transition-all duration-200 hover:from-primary-600 hover:to-primary-700 hover:scale-[1.02] active:scale-[0.98]">
              Talk to an expert
              <svg class="h-4 w-4 transition-transform group-hover:translate-x-1" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><path d="M5 12h14M13 5l7 7-7 7"/></svg>
            </a>
          </div>
        </div>
      </section>

      <!-- CTA -->
      <section class="relative z-10 mx-auto max-w-7xl px-4 py-20 sm:px-6 lg:px-8 lg:py-28">
        <div class="card-hover overflow-hidden rounded-3xl border border-slate-200/70 bg-gradient-to-br from-brand-50 via-white to-brand-50 p-8 backdrop-blur-md dark:border-white/10 dark:from-primary-500/10 dark:via-slate-900/50 dark:to-primary-600/10 lg:p-12" appReveal>
          <div class="mx-auto max-w-3xl text-center">
            <div class="mb-6 inline-flex items-center gap-2 rounded-full border border-brand-200/70 bg-white/70 px-3 py-1 text-xs font-semibold uppercase tracking-wider text-primary-700 backdrop-blur-sm dark:border-primary-500/30 dark:bg-slate-900/50 dark:text-brand-300">
              <svg class="h-3.5 w-3.5" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10"/><path d="M9 12l2 2 4-4"/></svg>
              Trusted by 100+ Zimbabwean businesses
            </div>

            <h2 class="text-3xl font-bold leading-tight tracking-tight text-slate-900 dark:text-white sm:text-4xl lg:text-5xl">
              Ready to simplify <span class="gradient-text">compliance?</span>
            </h2>

            <p class="mx-auto mt-4 max-w-xl text-base text-slate-600 dark:text-slate-300">
              Connect your Xero organisation to Fiscalize and automate ZIMRA compliance in minutes.
            </p>

            <div class="mt-8 flex flex-col items-center justify-center gap-3 sm:flex-row">
              @if (isAuthenticated()) {
                <a routerLink="/dashboard"
                   class="group inline-flex items-center justify-center gap-2 rounded-xl bg-gradient-to-r from-primary-500 to-primary-600 px-7 py-3.5 text-base font-semibold text-white shadow-xl shadow-primary-500/30 transition-all duration-200 hover:from-primary-600 hover:to-primary-700 hover:scale-[1.02] active:scale-[0.98]">
                  <span class="flex h-7 w-7 items-center justify-center rounded-full bg-white/20 text-xs font-bold">{{ initials() }}</span>
                  Go to Dashboard
                  <svg class="h-4 w-4 transition-transform group-hover:translate-x-0.5" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><path d="M5 12h14M13 5l7 7-7 7"/></svg>
                </a>
              } @else {
                <a routerLink="/login"
                   class="group inline-flex items-center justify-center gap-2 rounded-xl bg-gradient-to-r from-primary-500 to-primary-600 px-7 py-3.5 text-base font-semibold text-white shadow-xl shadow-primary-500/30 transition-all duration-200 hover:from-primary-600 hover:to-primary-700 hover:scale-[1.02] active:scale-[0.98]">
                  Sign in to get started
                  <svg class="h-4 w-4 transition-transform group-hover:translate-x-0.5" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><path d="M5 12h14M13 5l7 7-7 7"/></svg>
                </a>
                <a href="https://fiscalize.co.zw/sign-up" target="_blank" rel="noopener noreferrer"
                   class="inline-flex items-center justify-center rounded-xl border border-slate-200 bg-white/70 px-7 py-3.5 text-base font-semibold text-slate-700 backdrop-blur-sm transition-all duration-200 hover:bg-white hover:scale-[1.02] active:scale-[0.98] dark:border-white/10 dark:bg-white/5 dark:text-slate-200 dark:hover:bg-white/10">
                  Create a Fiscalize account
                </a>
              }
            </div>

            <div class="mx-auto mt-10 grid max-w-xl grid-cols-1 gap-3 sm:grid-cols-3">
              <div *ngFor="let b of trustBadges" class="flex items-center justify-center gap-2 rounded-xl border border-slate-200/70 bg-white/60 px-3 py-2.5 backdrop-blur-sm transition-colors hover:bg-white dark:border-white/10 dark:bg-slate-900/40 dark:hover:bg-slate-900/60">
                <svg class="h-4 w-4 flex-shrink-0 text-primary-600 dark:text-brand-400" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path [attr.d]="b.icon"></path></svg>
                <span class="text-sm font-medium text-slate-700 dark:text-slate-200">{{ b.label }}</span>
              </div>
            </div>
          </div>
        </div>
      </section>

      <!-- Footer -->
      <footer class="relative z-10 border-t border-slate-200/70 py-10 dark:border-white/5">
        <div class="container mx-auto px-4 lg:px-8">
          <div class="flex flex-col items-center justify-between gap-6 md:flex-row">
            <app-fiscalize-logo></app-fiscalize-logo>
            <div class="flex flex-wrap justify-center gap-6 text-sm font-medium text-slate-500 dark:text-slate-400">
              <a href="#features" class="transition-colors hover:text-primary-600 dark:hover:text-brand-400">Features</a>
              <a href="#how-it-works" class="transition-colors hover:text-primary-600 dark:hover:text-brand-400">How it works</a>
              <a href="https://fiscalize.co.zw/sign-up" target="_blank" rel="noopener noreferrer" class="transition-colors hover:text-primary-600 dark:hover:text-brand-400">Create account</a>
              @if (isAuthenticated()) {
                <a routerLink="/dashboard" class="transition-colors hover:text-primary-600 dark:hover:text-brand-400">Dashboard</a>
              } @else {
                <a routerLink="/login" class="transition-colors hover:text-primary-600 dark:hover:text-brand-400">Sign in</a>
              }
              <a routerLink="/terms" class="transition-colors hover:text-primary-600 dark:hover:text-brand-400">Terms</a>
              <a routerLink="/privacy" class="transition-colors hover:text-primary-600 dark:hover:text-brand-400">Privacy</a>
            </div>
            <p class="text-sm text-slate-400 dark:text-slate-500">© {{ year }} Fiscalize. All rights reserved.</p>
          </div>
        </div>
      </footer>

      <!-- Video modal (commented out until videos are ready) -->
      <!--
      <app-dialog [open]="videoOpen()" [title]="currentVideo()?.title || 'Tutorial'" size="xl" (close)="closeVideo()">
        <div class="relative aspect-video w-full overflow-hidden rounded-xl bg-black">
          <iframe *ngIf="currentVideo() as cv"
                  class="h-full w-full"
                  [src]="cv.url + '?autoplay=1'"
                  [title]="cv.title"
                  frameborder="0"
                  allow="accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture"
                  allowfullscreen>
          </iframe>
        </div>
      </app-dialog>
      -->
    </div>
  `
})
export class Landing {
  private authService = inject(AuthService);

  readonly isAuthenticated = this.authService.isAuthenticated;
  readonly currentUser = this.authService.currentUser;
  readonly initials = computed(() => {
    const name = this.currentUser()?.name || this.currentUser()?.email || 'U';
    return name.split(/\s+/).map((p) => p[0]).filter(Boolean).slice(0, 2).join('').toUpperCase();
  });

  readonly year = new Date().getFullYear();
  readonly scrolled = signal(false);
  readonly mobileMenuOpen = signal(false);
  // readonly videoOpen = signal(false);
  // readonly currentVideo = signal<Video | null>(null);
  readonly stars = [0, 1, 2, 3, 4];

  readonly navLinks = [
    { label: 'Features', href: '#features' },
    { label: 'How it works', href: '#how-it-works' },
    { label: 'Compatibility', href: '#compatibility' },
    { label: 'Pricing', href: '#pricing' },
    { label: 'Services', href: '#services' }
  ];

  readonly stats = [
    { value: '100+', label: 'Businesses' },
    { value: '99.9%', label: 'Accuracy' },
    { value: '5 min', label: 'Setup' },
    { value: '24/7', label: 'Support' }
  ];

  readonly features: Feature[] = [
    { title: '5-minute setup',         desc: 'Sign in to Xero, choose your organisation, and pick your ZIMRA fiscal device. No complex configuration required.', iconBg: 'from-primary-500 to-primary-600',     delay: 0,    icon: 'M12 8v4l3 2m6-2a9 9 0 11-18 0 9 9 0 0118 0z' },
    { title: 'AI-powered accuracy',    desc: 'Let AI automate template generation, HS Code assignment, and error-free compliance reporting. Smart automation you can trust.',    iconBg: 'from-violet-500 to-indigo-500', delay: 0.08, icon: 'M9.937 15.5A2 2 0 008.5 14.063l-6.135-1.582a.5.5 0 010-.962L8.5 9.936A2 2 0 009.937 8.5l1.582-6.135a.5.5 0 01.963 0L14.063 8.5A2 2 0 0015.5 9.937l6.135 1.581a.5.5 0 010 .964L15.5 14.063a2 2 0 00-1.437 1.437l-1.582 6.135a.5.5 0 01-.963 0z' },
    { title: 'Self-service simplicity',desc: 'No complex onboarding. Configure devices, manage invoices, and file directly with ZIMRA through a guided wizard.',          iconBg: 'from-amber-500 to-orange-500',  delay: 0.16, icon: 'M13 2L3 14h9l-1 8 10-12h-9l1-8z' },
    { title: 'Audit-ready always',     desc: 'Generate compliant fiscal invoices and reports that meet ZIMRA standards, ensuring peace of mind during audits.',                  iconBg: 'from-sky-500 to-brand-500',      delay: 0.24, icon: 'M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z' },
    { title: 'Trusted by businesses',  desc: '100+ Zimbabwean businesses rely on Fiscalize for seamless compliance, with a 99.9% accuracy rate and 24/7 support.',              iconBg: 'from-rose-500 to-pink-500',     delay: 0.32, icon: 'M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2 M16 3.128a4 4 0 0 1 0 7.744 M22 21v-2a4 4 0 0 0-3-3.87 M13 7a4 4 0 1 1-8 0 a4 4 0 1 1 8 0' },
    { title: 'ZIMRA compliant',        desc: 'Built to meet official ZIMRA fiscalization standards. Full compliance with ZIMRA requirements for fiscalization in Zimbabwe.',         iconBg: 'from-primary-500 to-primary-600',  delay: 0.4,  featured: true, icon: 'M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z M9 12l2 2 4-4' }
  ];

  readonly steps: Step[] = [
    { num: '01', title: 'Connect Xero',          desc: 'Sign in to Xero and allow access to one organisation. No technical expertise required.',            gradient: '', icon: 'M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z' },
    { num: '02', title: 'Connect & configure',   desc: 'Link your company profile, request fiscal devices, and submit templates through our guided wizard.', gradient: '', icon: 'M21 12a9 9 0 11-18 0 9 9 0 0118 0z M3.6 9h16.8M3.6 15h16.8M11.5 3a17 17 0 000 18M12.5 3a17 17 0 010 18' },
    { num: '03', title: 'Start fiscalizing',     desc: "Approve invoices in Xero as usual. Each one is fiscalised with ZIMRA and stamped. It's that simple.",              gradient: '', icon: 'M9 12l2 2 4-4m6 2a9 9 0 11-18 0 9 9 0 0118 0z' }
  ];

  /* readonly videos: Video[] = [
    { title: 'Getting started with Fiscalize', desc: 'Set up your account and connect to Xero in under 5 minutes.',                                       duration: '4:32', delay: 0,    thumb: 'https://img.youtube.com/vi/dQw4w9WgXcQ/maxresdefault.jpg', url: 'https://www.youtube.com/embed/dQw4w9WgXcQ' },
    { title: 'Fiscalizing your first invoice', desc: 'Step-by-step guide to creating and submitting your first ZIMRA-compliant invoice.',                       duration: '6:15', delay: 0.08, thumb: 'https://img.youtube.com/vi/dQw4w9WgXcQ/maxresdefault.jpg', url: 'https://www.youtube.com/embed/dQw4w9WgXcQ' },
    { title: 'Managing fiscal devices',        desc: 'Request, configure, and manage your fiscal devices efficiently.',                                          duration: '5:48', delay: 0.16, thumb: 'https://img.youtube.com/vi/dQw4w9WgXcQ/maxresdefault.jpg', url: 'https://www.youtube.com/embed/dQw4w9WgXcQ' },
    { title: 'Advanced features & tips',       desc: 'Discover power features and best practices to maximize your Fiscalize experience.',                       duration: '8:22', delay: 0.24, thumb: 'https://img.youtube.com/vi/dQw4w9WgXcQ/maxresdefault.jpg', url: 'https://www.youtube.com/embed/dQw4w9WgXcQ' }
  ]; */

  readonly testimonials: Testimonial[] = [
    { name: 'Tendai Moyo',        role: 'Finance Director', company: 'TechZim Solutions',    initials: 'TM', gradient: '', delay: 0,   text: 'Fiscalize has transformed how we handle ZIMRA compliance, and we have cut our invoicing time in half.' },
    { name: 'Rumbidzai Chikara',  role: 'Accountant',       company: 'Sunrise Imports',      initials: 'RC', gradient: '', delay: 0.1, text: 'The AI-powered accuracy is incredible. We have not had a single compliance issue since switching to Fiscalize. Highly recommended!' },
    { name: 'Kudzai Makoni',      role: 'CEO',              company: 'Harare Logistics Ltd', initials: 'KM', gradient: '', delay: 0.2, text: 'Setup took literally 5 minutes. The wizard guided us through everything. Now our team can focus on growing the business, not paperwork.' }
  ];

  readonly pricingFeatures = [
    'Xero invoices and credit notes',
    'ZIMRA-compliant fiscalization',
    'Unlimited fiscal invoices',
    'AI-powered template generation',
    'Fiscal device management',
    'Audit-ready reports',
    '24/7 support',
    'Free ZIMRA onboarding assistance'
  ];

  readonly services = [
    {
      title: 'Implementation',
      desc: 'End-to-end Xero and Fiscalize setup tailored to your business processes.',
      iconBg: 'from-primary-500 to-primary-600',
      icon: 'M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z',
      points: ['Account & company setup', 'Xero connection & tax rates', 'ZIMRA device registration', 'Staff training & handover']
    },
    {
      title: 'Customization',
      desc: 'Custom workflows, invoice templates, and integrations built around your unique requirements.',
      iconBg: 'from-violet-500 to-indigo-500',
      icon: 'M11 4H4a2 2 0 00-2 2v14a2 2 0 002 2h14a2 2 0 002-2v-7M18.5 2.5a2.121 2.121 0 013 3L12 15l-4 1 1-4 9.5-9.5z',
      points: ['Custom invoice & report layouts', 'Automated approval workflows', 'Third-party integrations', 'HS Code mapping rules']
    },
    {
      title: 'Ongoing Support',
      desc: 'Dedicated support to keep your compliance running smoothly — from troubleshooting to upgrades.',
      iconBg: 'from-amber-500 to-orange-500',
      icon: 'M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2M16 3.13a4 4 0 0 1 0 7.75M22 21v-2a4 4 0 0 0-3-3.87',
      points: ['Priority email & phone support', 'Help when Xero needs reconnecting', 'Compliance monitoring', 'Monthly health checks']
    }
  ];

  readonly freePlanItems: { label: string; check: boolean }[] = [
    { check: true, label: 'Create, approve and send invoices and credit notes exactly as you do now' },
    { check: true, label: 'Customers, items, tax rates and branding themes stay where they are' },
    { check: true, label: 'Payments and reconciliation are untouched' },
    { check: true, label: 'Disconnect at any time from the connection page' },
  ];

  readonly paidPlanItems: string[] = [
    'Approved invoices and credit notes fiscalised with ZIMRA, automatically or with one click',
    'A history note on each document with the receipt number and verification code',
    'Xero\'s own PDF with the ZIMRA stamp and QR code, attached to the document',
    'HS codes per item, and buyer details checked before anything is sent',
    'A clear reason and a fix when a document cannot be fiscalised',
  ];

  readonly trustBadges = [
    { label: '99.9% uptime',     icon: 'M23 6l-9.5 9.5-5-5L1 18' },
    { label: 'ZIMRA compliant',  icon: 'M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z' },
    { label: '24/7 support',     icon: 'M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2 M16 3.128a4 4 0 0 1 0 7.744 M22 21v-2a4 4 0 0 0-3-3.87 M13 7a4 4 0 1 1-8 0 a4 4 0 1 1 8 0' }
  ];

  @HostListener('window:scroll')
  onScroll(): void {
    if (typeof window === 'undefined') return;
    this.scrolled.set(window.scrollY > 24);
  }

  /* openVideo(v: Video): void {
    this.currentVideo.set(v);
    this.videoOpen.set(true);
  }

  closeVideo(): void {
    this.videoOpen.set(false);
    this.currentVideo.set(null);
  } */
}
