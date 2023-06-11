import { WelcomePageComponent } from './shell/welcome-page/welcome-page.component';
import { NgModule } from '@angular/core';
import { RouterModule, Routes } from '@angular/router';
import { AuthGuard } from './auth/auth-guard';
import { HomeComponent } from './home/home.component';

const routes: Routes = [
  {
    path:'',
    component:WelcomePageComponent
  },
  {
    path:'user-dashboard',
    loadChildren: () => import('./home/home.module').then(m => m.HomeModule),
    canActivate:[AuthGuard]
  },
  {
    path:'tarjetas',
    loadChildren: () => import('./tarjetas/tarjetas.module').then(m => m.TarjetasModule),
    canActivate:[AuthGuard]
  },
  {
    path:'cuentas',
    loadChildren: () => import('./cuentas/cuentas.module').then(m => m.CuentasModule),
    canActivate:[AuthGuard]
  },
  {
    path:'reportes',
    loadChildren: () => import('./reportes/reportes.module').then(m => m.ReportesModule),
    canActivate:[AuthGuard]
  },
  {
    path:'tarjetas',
    loadChildren: () => import('./tarjetas/tarjetas.module').then(m => m.TarjetasModule),
    canActivate:[AuthGuard]
  },
  {
    path:'ahorros',
    loadChildren: () => import('./ahorros/ahorros.module').then(m => m.AhorrosModule),
    canActivate:[AuthGuard]
  }
];

@NgModule({
  imports: [RouterModule.forRoot(routes, {useHash:true})],
  exports: [RouterModule]
})
export class AppRoutingModule { }
