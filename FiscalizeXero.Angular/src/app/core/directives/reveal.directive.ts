import { Directive, ElementRef, Input, OnDestroy, OnInit, inject } from '@angular/core';

@Directive({
  selector: '[appReveal]',
  standalone: true,
  host: { class: 'reveal' }
})
export class RevealDirective implements OnInit, OnDestroy {
  private host = inject(ElementRef<HTMLElement>);
  private observer?: IntersectionObserver;

  /** Optional delay in ms applied via inline transition-delay */
  @Input() revealDelay = 0;

  ngOnInit(): void {
    if (typeof window === 'undefined' || typeof IntersectionObserver === 'undefined') {
      this.host.nativeElement.classList.add('revealed');
      return;
    }
    if (this.revealDelay > 0) {
      this.host.nativeElement.style.transitionDelay = `${this.revealDelay}ms`;
    }
    this.observer = new IntersectionObserver((entries) => {
      for (const entry of entries) {
        if (entry.isIntersecting) {
          (entry.target as HTMLElement).classList.add('revealed');
          this.observer?.unobserve(entry.target);
        }
      }
    }, { threshold: 0.12 });
    this.observer.observe(this.host.nativeElement);
  }

  ngOnDestroy(): void {
    this.observer?.disconnect();
  }
}
