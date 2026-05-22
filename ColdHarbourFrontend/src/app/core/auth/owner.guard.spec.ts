import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { signal } from '@angular/core';
import { ownerGuard } from './owner.guard';
import { AuthService, UserRole } from './auth.service';

describe('ownerGuard', () => {
  let role: ReturnType<typeof signal<UserRole | null>>;
  let routerSpy: jasmine.SpyObj<Router>;

  function setUp(initialRole: UserRole | null) {
    role = signal<UserRole | null>(initialRole);
    routerSpy = jasmine.createSpyObj('Router', ['createUrlTree']);
    routerSpy.createUrlTree.and.returnValue({ __urlTree: true } as never);

    TestBed.configureTestingModule({
      providers: [
        { provide: Router, useValue: routerSpy },
        {
          provide: AuthService,
          useValue: {
            role,
            isOwner: () => role() === 'Owner',
          },
        },
      ],
    });
  }

  function runGuard() {
    return TestBed.runInInjectionContext(() =>
      ownerGuard(null as never, null as never),
    );
  }

  it('allows navigation when the user role is Owner', () => {
    setUp('Owner');
    expect(runGuard()).toBeTrue();
    expect(routerSpy.createUrlTree).not.toHaveBeenCalled();
  });

  it('redirects to /home when the user role is User', () => {
    setUp('User');
    runGuard();
    expect(routerSpy.createUrlTree).toHaveBeenCalledWith(['/home']);
  });

  it('redirects to /home when the role is null (unknown)', () => {
    setUp(null);
    runGuard();
    expect(routerSpy.createUrlTree).toHaveBeenCalledWith(['/home']);
  });
});
