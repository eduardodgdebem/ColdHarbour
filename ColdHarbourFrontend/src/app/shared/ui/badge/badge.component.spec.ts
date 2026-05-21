import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Component, signal } from '@angular/core';
import { By } from '@angular/platform-browser';
import { BadgeComponent, BadgeVariant } from './badge.component';

@Component({
  standalone: true,
  imports: [BadgeComponent],
  template: `<app-badge [variant]="variant()">{{ label() }}</app-badge>`,
})
class HostComponent {
  variant = signal<BadgeVariant>('default');
  label = signal('PLAYING');
}

describe('BadgeComponent', () => {
  let fixture: ComponentFixture<HostComponent>;
  let host: HostComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [HostComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(HostComponent);
    host = fixture.componentInstance;
    fixture.detectChanges();
  });

  function badgeEl(): HTMLElement {
    return fixture.debugElement.query(By.css('.badge')).nativeElement;
  }

  it('renders the projected label content', () => {
    expect(badgeEl().textContent?.trim()).toBe('PLAYING');
  });

  it('applies the default variant class by default', () => {
    expect(badgeEl().classList).toContain('badge--default');
  });

  it('applies the active variant class when variant=active', () => {
    host.variant.set('active');
    fixture.detectChanges();
    expect(badgeEl().classList).toContain('badge--active');
  });

  it('applies the accent variant class when variant=accent', () => {
    host.variant.set('accent');
    fixture.detectChanges();
    expect(badgeEl().classList).toContain('badge--accent');
  });
});
