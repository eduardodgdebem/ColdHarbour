import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { Router, provideRouter } from '@angular/router';
import { NotFoundPageComponent } from './not-found-page.component';

describe('NotFoundPageComponent', () => {
  let fixture: ComponentFixture<NotFoundPageComponent>;
  let routerNavigateSpy: jasmine.Spy;

  function setUp() {
    TestBed.configureTestingModule({
      imports: [NotFoundPageComponent],
      providers: [provideRouter([{ path: 'home', children: [] }])],
    });

    const router = TestBed.inject(Router);
    routerNavigateSpy = spyOn(router, 'navigate').and.returnValue(
      Promise.resolve(true),
    );

    fixture = TestBed.createComponent(NotFoundPageComponent);
    fixture.detectChanges();
  }

  it('renders a large 404 headline', () => {
    setUp();
    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('404');
  });

  it('renders a brutalist not-found message', () => {
    setUp();
    const text = (fixture.nativeElement.textContent as string).toUpperCase();
    expect(text).toContain('NOT FOUND');
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
