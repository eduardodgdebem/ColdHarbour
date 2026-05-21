import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Component, signal } from '@angular/core';
import { By } from '@angular/platform-browser';
import { CardComponent, CardPadding } from './card.component';

@Component({
  standalone: true,
  imports: [CardComponent],
  template: `
    <app-card [padding]="padding()" [shadow]="shadow()">
      @if (withHeader()) {
        <div slot="header">HEADER</div>
      }
      <p>Body content</p>
      @if (withFooter()) {
        <div slot="footer">FOOTER</div>
      }
    </app-card>
  `,
})
class HostComponent {
  padding = signal<CardPadding>('md');
  shadow = signal(false);
  withHeader = signal(false);
  withFooter = signal(false);
}

describe('CardComponent', () => {
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

  function cardEl(): HTMLElement {
    return fixture.debugElement.query(By.css('.card')).nativeElement;
  }

  it('renders the projected body content', () => {
    expect(cardEl().textContent).toContain('Body content');
  });

  it('applies the medium padding class by default', () => {
    expect(cardEl().classList).toContain('card--padding-md');
  });

  it('applies the small padding class when padding=sm', () => {
    host.padding.set('sm');
    fixture.detectChanges();
    expect(cardEl().classList).toContain('card--padding-sm');
  });

  it('applies the large padding class when padding=lg', () => {
    host.padding.set('lg');
    fixture.detectChanges();
    expect(cardEl().classList).toContain('card--padding-lg');
  });

  it('does not include the shadow modifier by default', () => {
    expect(cardEl().classList).not.toContain('card--shadow');
  });

  it('adds the shadow modifier when shadow=true', () => {
    host.shadow.set(true);
    fixture.detectChanges();
    expect(cardEl().classList).toContain('card--shadow');
  });

  it('hides the header section when no header content is projected', () => {
    const header: HTMLElement = fixture.debugElement.query(
      By.css('.card__header'),
    ).nativeElement;
    expect(header.textContent?.trim() ?? '').toBe('');
    expect(window.getComputedStyle(header).display).toBe('none');
  });

  it('renders the header section when header content is projected', () => {
    host.withHeader.set(true);
    fixture.detectChanges();
    const header: HTMLElement = fixture.debugElement.query(
      By.css('.card__header'),
    ).nativeElement;
    expect(header.textContent?.trim()).toBe('HEADER');
    expect(window.getComputedStyle(header).display).not.toBe('none');
  });

  it('hides the footer section when no footer content is projected', () => {
    const footer: HTMLElement = fixture.debugElement.query(
      By.css('.card__footer'),
    ).nativeElement;
    expect(window.getComputedStyle(footer).display).toBe('none');
  });

  it('renders the footer section when footer content is projected', () => {
    host.withFooter.set(true);
    fixture.detectChanges();
    const footer: HTMLElement = fixture.debugElement.query(
      By.css('.card__footer'),
    ).nativeElement;
    expect(footer.textContent?.trim()).toBe('FOOTER');
    expect(window.getComputedStyle(footer).display).not.toBe('none');
  });
});
