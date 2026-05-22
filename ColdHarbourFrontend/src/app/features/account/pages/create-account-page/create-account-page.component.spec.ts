import { ComponentFixture, TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { By } from '@angular/platform-browser';
import { Router, provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { CreateAccountPageComponent } from './create-account-page.component';
import { AuthService } from '../../../../core/auth/auth.service';

describe('CreateAccountPageComponent', () => {
  let fixture: ComponentFixture<CreateAccountPageComponent>;
  let component: CreateAccountPageComponent;
  let registerSpy: jasmine.Spy;
  let routerNavigateSpy: jasmine.Spy;

  function setUp() {
    registerSpy = jasmine.createSpy('register').and.returnValue(of(void 0));

    const authStub = {
      email: signal<string | null>('owner@example.com'),
      name: signal<string | null>(null),
      role: signal('Owner'),
      register: registerSpy,
    };

    TestBed.configureTestingModule({
      imports: [CreateAccountPageComponent],
      providers: [
        provideRouter([
          { path: 'account', children: [] },
          { path: 'home', children: [] },
        ]),
        { provide: AuthService, useValue: authStub },
      ],
    });

    const router = TestBed.inject(Router);
    routerNavigateSpy = spyOn(router, 'navigate').and.returnValue(
      Promise.resolve(true),
    );

    fixture = TestBed.createComponent(CreateAccountPageComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  it('renders email, password, confirm password fields', () => {
    setUp();
    const inputs = fixture.debugElement.queryAll(By.css('app-input'));
    expect(inputs.length).toBeGreaterThanOrEqual(3);
  });

  it('rejects submission when passwords do not match', () => {
    setUp();
    component.form.setValue({
      name: '',
      email: 'new@example.com',
      password: 'password-123',
      confirmPassword: 'different',
    });
    component.submit();
    expect(registerSpy).not.toHaveBeenCalled();
    expect(component.error()).toContain('match');
  });

  it('rejects submission when the form is invalid', () => {
    setUp();
    component.form.setValue({
      name: '',
      email: 'not-an-email',
      password: 'short',
      confirmPassword: 'short',
    });
    component.submit();
    expect(registerSpy).not.toHaveBeenCalled();
  });

  it('calls authService.register with the form fields on submit', () => {
    setUp();
    component.form.setValue({
      name: 'New Listener',
      email: 'new@example.com',
      password: 'password-123',
      confirmPassword: 'password-123',
    });
    component.submit();
    expect(registerSpy).toHaveBeenCalledWith({
      name: 'New Listener',
      email: 'new@example.com',
      password: 'password-123',
    });
  });

  it('omits the name field from the payload when blank', () => {
    setUp();
    component.form.setValue({
      name: '',
      email: 'new@example.com',
      password: 'password-123',
      confirmPassword: 'password-123',
    });
    component.submit();
    expect(registerSpy).toHaveBeenCalledWith({
      email: 'new@example.com',
      password: 'password-123',
    });
  });

  it('navigates to /account on success', () => {
    setUp();
    component.form.setValue({
      name: '',
      email: 'new@example.com',
      password: 'password-123',
      confirmPassword: 'password-123',
    });
    component.submit();
    expect(routerNavigateSpy).toHaveBeenCalledWith(['/account']);
  });

  it('surfaces a registration failure as an error message', () => {
    setUp();
    registerSpy.and.returnValue(throwError(() => new Error('boom')));
    component.form.setValue({
      name: '',
      email: 'new@example.com',
      password: 'password-123',
      confirmPassword: 'password-123',
    });
    component.submit();
    expect(component.error()).toBeTruthy();
    expect(routerNavigateSpy).not.toHaveBeenCalled();
  });
});
