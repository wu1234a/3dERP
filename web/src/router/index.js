import { createRouter, createWebHistory } from 'vue-router'
import DashboardView from '../views/DashboardView.vue'
import MasterDataView from '../views/MasterDataView.vue'
import ModuleView from '../views/ModuleView.vue'

const routes = [
  {
    path: '/',
    name: 'dashboard',
    component: DashboardView,
    meta: { title: '总览' }
  },
  {
    path: '/master-data',
    name: 'master-data',
    component: MasterDataView,
    meta: {
      title: '主数据',
      description: '客户、供应商、物料、仓库、计量单位与工艺参数。'
    }
  },
  {
    path: '/sales',
    name: 'sales',
    component: ModuleView,
    meta: {
      title: '销售',
      description: '报价、销售订单、发货与应收联动。'
    }
  },
  {
    path: '/purchasing',
    name: 'purchasing',
    component: ModuleView,
    meta: {
      title: '采购',
      description: '采购申请、采购订单、收货与应付联动。'
    }
  },
  {
    path: '/production',
    name: 'production',
    component: ModuleView,
    meta: {
      title: '生产',
      description: '3D 打印任务、排产、材料耗用、设备与后处理。'
    }
  },
  {
    path: '/inventory',
    name: 'inventory',
    component: ModuleView,
    meta: {
      title: '库存',
      description: '库存流水、调拨、盘点与可用量。'
    }
  },
  {
    path: '/finance',
    name: 'finance',
    component: ModuleView,
    meta: {
      title: '财务',
      description: '应收、应付、总账与凭证。'
    }
  },
  {
    path: '/settings',
    name: 'settings',
    component: ModuleView,
    meta: {
      title: '系统设置',
      description: '用户、角色、权限、编号规则与系统参数。'
    }
  }
]

const router = createRouter({
  history: createWebHistory(),
  routes
})

router.afterEach((to) => {
  document.title = `${to.meta.title || '3dERP'} · 3dERP`
})

export default router
