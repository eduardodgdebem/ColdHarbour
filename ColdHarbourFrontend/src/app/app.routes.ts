import { Routes } from '@angular/router';
import { HomePageComponent } from './features/home/pages/home-page/home-page.component';
import { LibraryPageComponent } from './features/library/pages/library-page/library-page.component';
import { AlbumsPageComponent } from './features/browse/pages/albums-page/albums-page.component';
import { AlbumDetailPageComponent } from './features/browse/pages/album-detail-page/album-detail-page.component';
import { ArtistsPageComponent } from './features/browse/pages/artists-page/artists-page.component';
import { ArtistDetailPageComponent } from './features/browse/pages/artist-detail-page/artist-detail-page.component';
import { LoginPageComponent } from './features/auth/pages/login-page/login-page.component';
import { DevicesPageComponent } from './features/devices/pages/devices-page/devices-page.component';
import { AccountPageComponent } from './features/account/pages/account-page/account-page.component';
import { CreateAccountPageComponent } from './features/account/pages/create-account-page/create-account-page.component';
import { PlayerPageComponent } from './features/player/pages/player-page/player-page.component';
import { ErrorPageComponent } from './features/errors/pages/error-page/error-page.component';
import { NotFoundPageComponent } from './features/errors/pages/not-found-page/not-found-page.component';
import { authGuard } from './core/auth/auth.guard';
import { ownerGuard } from './core/auth/owner.guard';

export const routes: Routes = [
  {
    path: 'login',
    component: LoginPageComponent,
  },
  {
    path: 'home',
    component: HomePageComponent,
    canActivate: [authGuard],
  },
  {
    path: 'library',
    component: LibraryPageComponent,
    canActivate: [authGuard],
  },
  {
    path: 'albums',
    component: AlbumsPageComponent,
    canActivate: [authGuard],
  },
  {
    path: 'albums/:id',
    component: AlbumDetailPageComponent,
    canActivate: [authGuard],
  },
  {
    path: 'artists',
    component: ArtistsPageComponent,
    canActivate: [authGuard],
  },
  {
    path: 'artists/:id',
    component: ArtistDetailPageComponent,
    canActivate: [authGuard],
  },
  {
    path: 'devices',
    component: DevicesPageComponent,
    canActivate: [authGuard],
  },
  {
    path: 'account',
    component: AccountPageComponent,
    canActivate: [authGuard],
  },
  {
    path: 'create-account',
    component: CreateAccountPageComponent,
    canActivate: [authGuard, ownerGuard],
  },
  {
    path: 'player',
    component: PlayerPageComponent,
    canActivate: [authGuard],
  },
  {
    path: 'error',
    component: ErrorPageComponent,
  },
  {
    path: '',
    redirectTo: '/home',
    pathMatch: 'full',
  },
  {
    path: '**',
    component: NotFoundPageComponent,
  },
];
