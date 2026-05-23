import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { ActivatedRoute, Router, convertToParamMap } from '@angular/router';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { ErrorPageComponent } from './error-page.component';

describe('ErrorPageComponent', () => {
  let fixture: ComponentFixture<ErrorPageComponent>;
  let routerNavigateSpy: jasmine.Spy;

  function setUp(queryParams: Record<string, string> = {}) {
    const routeStub = {
      queryParamMap: of(convertToParamMap(queryParams)),
    };

    TestBed.configureTestingModule({
      imports: [ErrorPageComponent],
      providers: [
        provideRouter([{ path: 'home', children: [] }]),
        { provide: ActivatedRoute, useValue: routeStub },
      ],
    });

    const router = TestBed.inject(Router);
    routerNavigateSpy = spyOn(router, 'navigate').and.returnValue(
      Promise.resolve(true),
    );

    fixture = TestBed.createComponent(ErrorPageComponent);
    fixture.detectChanges();
  }

  it('renders a default unexpected-error message when no params provided', () => {
    setUp();
    const text = (fixture.nativeElement.textContent as string).toUpperCase();
    expect(text).toContain('UNEXPECTED');
  });

  it('renders the code from the query param', () => {
    setUp({ code: 'SERVER', message: 'API down' });
    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('SERVER');
  });

  it('renders the message from the query param', () => {
    setUp({ code: 'SERVER', message: 'API down' });
    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('API down');
  });

  it('renders a back-to-home action button', () => {
    setUp();
    const button = fixture.debugElement.query(By.css('app-button'));
    expect(button).toBeTruthy();
  });

  it('navigates to /home when the back-to-home button is clicked', () => {
    setUp();
    const button = fixture.debugElement.query(By.css('app-button'));
    button.triggerEventHandler('click', new MouseEvent('click'));
    expect(routerNavigateSpy).toHaveBeenCalledWith(['/home']);
  });
});
