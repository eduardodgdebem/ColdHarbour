import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Component, signal } from '@angular/core';
import { By } from '@angular/platform-browser';
import { ButtonComponent } from './button.component';

@Component({
  standalone: true,
  imports: [ButtonComponent],
  template: `
    <app-button
      [variant]="variant()"
      [size]="size()"
      [type]="type()"
      [disabled]="disabled()"
      [loading]="loading()"
      (click)="onClick()"
    >
      {{ label() }}
    </app-button>
  `,
})
class HostComponent {
  variant = signal<'default' | 'primary' | 'danger'>('default');
  size = signal<'sm' | 'md'>('md');
  type = signal<'button' | 'submit'>('button');
  disabled = signal(false);
  loading = signal(false);
  label = signal('Click me');
  clicks = 0;
  onClick() {
    this.clicks++;
  }
}

describe('ButtonComponent', () => {
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

  function buttonEl(): HTMLButtonElement {
    return fixture.debugElement.query(By.css('button')).nativeElement;
  }

  it('renders projected label content', () => {
    expect(buttonEl().textContent?.trim()).toBe('Click me');
  });

  it('applies the default variant class by default', () => {
    expect(buttonEl().classList).toContain('btn');
    expect(buttonEl().classList).toContain('btn--default');
  });

  it('applies the primary variant class when variant=primary', () => {
    host.variant.set('primary');
    fixture.detectChanges();
    expect(buttonEl().classList).toContain('btn--primary');
  });

  it('applies the danger variant class when variant=danger', () => {
    host.variant.set('danger');
    fixture.detectChanges();
    expect(buttonEl().classList).toContain('btn--danger');
  });

  it('applies the sm size modifier when size=sm', () => {
    host.size.set('sm');
    fixture.detectChanges();
    expect(buttonEl().classList).toContain('btn--sm');
  });

  it('uses type=button by default', () => {
    expect(buttonEl().type).toBe('button');
  });

  it('uses type=submit when specified', () => {
    host.type.set('submit');
    fixture.detectChanges();
    expect(buttonEl().type).toBe('submit');
  });

  it('reflects disabled input on the native element', () => {
    host.disabled.set(true);
    fixture.detectChanges();
    expect(buttonEl().disabled).toBeTrue();
  });

  it('is disabled while loading regardless of the disabled input', () => {
    host.loading.set(true);
    fixture.detectChanges();
    expect(buttonEl().disabled).toBeTrue();
  });

  it('renders a loading indicator and hides projected content when loading', () => {
    host.loading.set(true);
    fixture.detectChanges();
    const text = buttonEl().textContent?.trim() ?? '';
    expect(text).toContain('…');
    expect(text).not.toContain('Click me');
  });

  it('emits click events when clicked', () => {
    buttonEl().click();
    expect(host.clicks).toBe(1);
  });

  it('does not emit click events when disabled', () => {
    host.disabled.set(true);
    fixture.detectChanges();
    buttonEl().click();
    expect(host.clicks).toBe(0);
  });

  it('does not emit click events when loading', () => {
    host.loading.set(true);
    fixture.detectChanges();
    buttonEl().click();
    expect(host.clicks).toBe(0);
  });
});
