import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Component, signal } from '@angular/core';
import { Location } from '@angular/common';
import { By } from '@angular/platform-browser';
import {
  BackButtonComponent,
  BackButtonVariant,
} from './back-button.component';

@Component({
  standalone: true,
  imports: [BackButtonComponent],
  template: ` <app-back-button [variant]="variant()" [label]="label()" /> `,
})
class HostComponent {
  variant = signal<BackButtonVariant>('default');
  label = signal<string>('Back');
}

describe('BackButtonComponent', () => {
  let fixture: ComponentFixture<HostComponent>;
  let host: HostComponent;
  let locationSpy: jasmine.SpyObj<Location>;

  beforeEach(async () => {
    locationSpy = jasmine.createSpyObj('Location', ['back']);

    await TestBed.configureTestingModule({
      imports: [HostComponent],
      providers: [{ provide: Location, useValue: locationSpy }],
    }).compileComponents();

    fixture = TestBed.createComponent(HostComponent);
    host = fixture.componentInstance;
    fixture.detectChanges();
  });

  function btnEl(): HTMLButtonElement {
    return fixture.debugElement.query(By.css('button')).nativeElement;
  }

  it('renders "← BACK" by default', () => {
    expect(btnEl().textContent?.trim()).toBe('← BACK');
  });

  it('renders a custom label when provided', () => {
    host.label.set('Home');
    fixture.detectChanges();
    expect(btnEl().textContent?.trim()).toBe('← HOME');
  });

  it('calls Location.back() when clicked', () => {
    btnEl().click();
    expect(locationSpy.back).toHaveBeenCalledTimes(1);
  });

  it('applies the default variant class by default', () => {
    expect(btnEl().classList).toContain('back-btn--default');
  });

  it('applies the inverse variant class when variant=inverse', () => {
    host.variant.set('inverse');
    fixture.detectChanges();
    expect(btnEl().classList).toContain('back-btn--inverse');
  });
});
