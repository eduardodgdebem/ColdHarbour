import { ComponentFixture, TestBed } from '@angular/core/testing';
import { signal, computed } from '@angular/core';
import { By } from '@angular/platform-browser';
import { Router, provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { AccountPageComponent } from './account-page.component';
import { AuthService, UserRole } from '../../../../core/auth/auth.service';
import type { Music, Playlist } from '../../../../core/api/api.service';
import { MusicService } from '../../../player/services/music.service';

describe('AccountPageComponent', () => {
  let fixture: ComponentFixture<AccountPageComponent>;
  let component: AccountPageComponent;
  let logoutSpy: jasmine.Spy;
  let changePasswordSpy: jasmine.Spy;
  let routerSpy: jasmine.SpyObj<Router>;

  function setUp(
    opts: {
      email?: string | null;
      name?: string | null;
      role?: UserRole | null;
    } = {},
  ) {
    const email = signal<string | null>(opts.email ?? 'user@example.com');
    const name = signal<string | null>(opts.name ?? null);
    const role = signal<UserRole | null>(opts.role ?? 'User');

    logoutSpy = jasmine.createSpy('logout').and.returnValue(of(void 0));
    changePasswordSpy = jasmine
      .createSpy('changePassword')
      .and.returnValue(of(void 0));

    const authStub = {
      email,
      name,
      role,
      isOwner: computed(() => role() === 'Owner'),
      logout: logoutSpy,
      changePassword: changePasswordSpy,
    };

    TestBed.configureTestingModule({
      imports: [AccountPageComponent],
      providers: [
        provideRouter([
          { path: 'create-account', children: [] },
          { path: 'login', children: [] },
        ]),
        { provide: AuthService, useValue: authStub },
        {
          provide: MusicService,
          useValue: {
            currentMusic: signal<Music | null>(null),
            currentPlayList: signal<Playlist | null>(null),
            isLoading: signal(false),
            error: signal<string | null>(null),
          },
        },
      ],
    });

    const router = TestBed.inject(Router);
    routerSpy = router as unknown as jasmine.SpyObj<Router>;
    spyOn(router, 'navigate').and.returnValue(Promise.resolve(true));

    fixture = TestBed.createComponent(AccountPageComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  it('renders the user email', () => {
    setUp({ email: 'eduardo@example.com' });
    const profile = fixture.debugElement.query(By.css('.profile'));
    expect(profile.nativeElement.textContent).toContain('eduardo@example.com');
  });

  it('renders the role badge', () => {
    setUp({ role: 'Owner' });
    const roleEl = fixture.debugElement.query(By.css('.profile__role'));
    expect(roleEl.nativeElement.textContent.trim()).toContain('OWNER');
  });

  it('shows the CREATE USER link when the user is an Owner', () => {
    setUp({ role: 'Owner' });
    const link = fixture.debugElement.query(
      By.css('[data-test="create-user-link"]'),
    );
    expect(link).toBeTruthy();
  });

  it('hides the CREATE USER link for non-owners', () => {
    setUp({ role: 'User' });
    expect(
      fixture.debugElement.query(By.css('[data-test="create-user-link"]')),
    ).toBeNull();
  });

  it('hides the CREATE USER link when role is unknown', () => {
    setUp({ role: null });
    expect(
      fixture.debugElement.query(By.css('[data-test="create-user-link"]')),
    ).toBeNull();
  });

  it('calls authService.logout and navigates to /login when sign out is clicked', () => {
    setUp();
    component.signOut();
    expect(logoutSpy).toHaveBeenCalled();
    expect(routerSpy.navigate).toHaveBeenCalledWith(['/login']);
  });

  it('submits the change-password form when both fields are filled and match', () => {
    setUp();
    component.passwordForm.setValue({
      currentPassword: 'old-password',
      newPassword: 'new-password-123',
      confirmPassword: 'new-password-123',
    });
    component.submitPasswordChange();
    expect(changePasswordSpy).toHaveBeenCalledWith(
      'old-password',
      'new-password-123',
    );
  });

  it('does not submit when the new password does not match the confirmation', () => {
    setUp();
    component.passwordForm.setValue({
      currentPassword: 'old-password',
      newPassword: 'new-password-123',
      confirmPassword: 'different',
    });
    component.submitPasswordChange();
    expect(changePasswordSpy).not.toHaveBeenCalled();
    expect(component.passwordError()).toContain('match');
  });

  it('surfaces a password-change failure as an error message', () => {
    setUp();
    changePasswordSpy.and.returnValue(throwError(() => new Error('boom')));
    component.passwordForm.setValue({
      currentPassword: 'old',
      newPassword: 'new-password-123',
      confirmPassword: 'new-password-123',
    });
    component.submitPasswordChange();
    expect(component.passwordError()).toBeTruthy();
  });
});
