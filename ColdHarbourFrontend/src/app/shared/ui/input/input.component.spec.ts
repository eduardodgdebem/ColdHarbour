import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Component, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { By } from '@angular/platform-browser';
import { InputComponent, InputType } from './input.component';

@Component({
  standalone: true,
  imports: [InputComponent, ReactiveFormsModule],
  template: `
    <app-input
      [type]="type()"
      [placeholder]="placeholder()"
      [autocomplete]="autocomplete()"
      [formControl]="control"
    />
  `,
})
class HostComponent {
  type = signal<InputType>('text');
  placeholder = signal('Type here');
  autocomplete = signal('off');
  control = new FormControl('');
}

describe('InputComponent', () => {
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

  function inputEl(): HTMLInputElement {
    return fixture.debugElement.query(By.css('input')).nativeElement;
  }

  it('renders an input with the configured type', () => {
    host.type.set('email');
    fixture.detectChanges();
    expect(inputEl().type).toBe('email');
  });

  it('defaults to type=text when type is not specified', () => {
    expect(inputEl().type).toBe('text');
  });

  it('forwards the placeholder attribute', () => {
    expect(inputEl().placeholder).toBe('Type here');
  });

  it('forwards the autocomplete attribute', () => {
    host.autocomplete.set('email');
    fixture.detectChanges();
    expect(inputEl().getAttribute('autocomplete')).toBe('email');
  });

  it('writes the control value to the input', () => {
    host.control.setValue('hello');
    fixture.detectChanges();
    expect(inputEl().value).toBe('hello');
  });

  it('updates the control when the user types', () => {
    inputEl().value = 'user@example.com';
    inputEl().dispatchEvent(new Event('input'));
    expect(host.control.value).toBe('user@example.com');
  });

  it('marks the control as touched on blur', () => {
    expect(host.control.touched).toBeFalse();
    inputEl().dispatchEvent(new Event('blur'));
    expect(host.control.touched).toBeTrue();
  });

  it('disables the native input when the form control is disabled', () => {
    host.control.disable();
    fixture.detectChanges();
    expect(inputEl().disabled).toBeTrue();
  });
});
