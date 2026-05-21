import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Component, signal } from '@angular/core';
import { By } from '@angular/platform-browser';
import { FormFieldComponent } from './form-field.component';

@Component({
  standalone: true,
  imports: [FormFieldComponent],
  template: `
    <app-form-field
      [label]="label()"
      [errorMessage]="errorMessage()"
      [hint]="hint()"
      [forId]="forId()"
    >
      <input id="my-input" />
    </app-form-field>
  `,
})
class HostComponent {
  label = signal('Email');
  errorMessage = signal<string | null>(null);
  hint = signal<string | null>(null);
  forId = signal('my-input');
}

describe('FormFieldComponent', () => {
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

  it('renders the label text', () => {
    const label = fixture.debugElement.query(By.css('.form-field__label'));
    expect(label.nativeElement.textContent.trim()).toBe('Email');
  });

  it('associates the label with the projected control via forId', () => {
    const label: HTMLLabelElement = fixture.debugElement.query(
      By.css('.form-field__label'),
    ).nativeElement;
    expect(label.htmlFor).toBe('my-input');
  });

  it('projects the input control into the field', () => {
    const projectedInput = fixture.debugElement.query(By.css('input#my-input'));
    expect(projectedInput).toBeTruthy();
  });

  it('does not render the error element when errorMessage is null', () => {
    expect(fixture.debugElement.query(By.css('.form-field__error'))).toBeNull();
  });

  it('renders the error element when errorMessage is set', () => {
    host.errorMessage.set('Required');
    fixture.detectChanges();
    const error = fixture.debugElement.query(By.css('.form-field__error'));
    expect(error.nativeElement.textContent.trim()).toBe('Required');
  });

  it('renders the hint when provided and no error', () => {
    host.hint.set('We never share your email.');
    fixture.detectChanges();
    const hint = fixture.debugElement.query(By.css('.form-field__hint'));
    expect(hint.nativeElement.textContent.trim()).toBe(
      'We never share your email.',
    );
  });

  it('hides the hint while an error is showing', () => {
    host.hint.set('Helper text');
    host.errorMessage.set('Required');
    fixture.detectChanges();
    expect(fixture.debugElement.query(By.css('.form-field__hint'))).toBeNull();
    expect(
      fixture.debugElement.query(By.css('.form-field__error')),
    ).toBeTruthy();
  });
});
