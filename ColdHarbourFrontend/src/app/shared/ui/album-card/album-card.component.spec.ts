import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Component } from '@angular/core';
import { By } from '@angular/platform-browser';
import { provideRouter } from '@angular/router';
import { AlbumCardComponent } from './album-card.component';

@Component({
  standalone: true,
  imports: [AlbumCardComponent],
  template: `<app-album-card
    [title]="title"
    [subtitle]="subtitle"
    [meta]="meta"
    [coverRef]="coverRef"
    [link]="link"
  />`,
})
class HostComponent {
  title = 'The Wall';
  subtitle = 'Pink Floyd';
  meta = '2 TRACKS';
  coverRef = '/api/artwork/album-1?size=256';
  link: string | unknown[] = ['/albums', 'album-1'];
}

describe('AlbumCardComponent', () => {
  let fixture: ComponentFixture<HostComponent>;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HostComponent],
      providers: [provideRouter([])],
    });
    fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();
  });

  it('renders title, subtitle and meta', () => {
    const el = fixture.nativeElement as HTMLElement;
    expect(el.textContent).toContain('The Wall');
    expect(el.textContent).toContain('Pink Floyd');
    expect(el.textContent).toContain('2 TRACKS');
  });

  it('links to the provided route', () => {
    const link = fixture.debugElement.query(By.css('a.album-card'))
      .nativeElement as HTMLAnchorElement;
    expect(link.getAttribute('href')).toBe('/albums/album-1');
  });

  it('renders the cover image when coverRef is set', () => {
    const img = fixture.debugElement.query(By.css('img'));
    expect(img).toBeTruthy();
    expect(img.nativeElement.getAttribute('src')).toContain(
      '/api/artwork/album-1',
    );
  });

  it('falls back to a placeholder when the image errors', () => {
    const img = fixture.debugElement.query(By.css('img'))
      .nativeElement as HTMLImageElement;
    img.dispatchEvent(new Event('error'));
    fixture.detectChanges();
    expect(fixture.debugElement.query(By.css('img'))).toBeNull();
    expect(
      fixture.debugElement.query(By.css('.album-card__art-placeholder')),
    ).toBeTruthy();
  });
});
